using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Windows.Forms;
using System.Runtime.CompilerServices; // Necessário para o NoInlining
using NXOpen;
using NXOpen.CAM;

namespace NXDeflectionPlugin
{
    public class ToolDeflectionAnalyzer
    {
        private Session theSession;
        private UI theUI;
        private PhysicsCalculator physicsCalc; 

        public ToolDeflectionAnalyzer()
        {
            theSession = Session.GetSession();
            theUI = UI.GetUI();
            physicsCalc = new PhysicsCalculator(); 
        }

        public Operation GetSelectedOperation()
        {
            int numSelected = theUI.SelectionManager.GetNumSelectedObjects();
            if (numSelected == 0) throw new Exception("Selecione uma operação CAM antes de rodar o plugin.");
            
            TaggedObject selObj = theUI.SelectionManager.GetSelectedTaggedObject(0);
            Operation op = selObj as Operation;
            if (op == null) throw new Exception("O objeto selecionado não é uma operação de usinagem válida.");
                
            return op;
        }

        /// <summary>
        /// Extrai os pontos da trajetória via OutputClsf e transforma do MCS para o ACS.
        /// O arquivo CLS contém uma linha MSYS/ com a origem e orientação do MCS em ACS —
        /// essa informação é usada para aplicar a transformação correta, independente de
        /// como o eixo de coordenadas da máquina está posicionado/rotacionado na peça.
        /// </summary>
        public double[][] ExtractRealPathPoints(Operation op)
        {
            List<double[]> pointsList = new List<double[]>();
            string tempFile = Path.Combine(Path.GetTempPath(), "toolpath_temp.cls");

            try
            {
                CAMSetup camSetup = theSession.Parts.Work.CAMSetup;
                camSetup.OutputClsf(new CAMObject[] { op }, "CLSF_STANDARD", tempFile, CAMSetup.OutputUnits.Metric);

                if (File.Exists(tempFile))
                {
                    string[] lines = File.ReadAllLines(tempFile);

                    // Transformação MCS → ACS: identidade por padrão
                    // (caso o arquivo não tenha MSYS/, os pontos ficam no MCS original)
                    double[] mcsOrigin = new double[] { 0, 0, 0 };
                    double[] mcsX = new double[] { 1, 0, 0 }; // eixo X do MCS em ACS
                    double[] mcsY = new double[] { 0, 1, 0 }; // eixo Y do MCS em ACS
                    double[] mcsZ = new double[] { 0, 0, 1 }; // eixo Z = X × Y

                    foreach (string line in lines)
                    {
                        // MSYS/ ox,oy,oz, xx,xy,xz, yx,yy,yz
                        // Define a origem e os 2 primeiros eixos do MCS em coordenadas ACS
                        if (line.TrimStart().StartsWith("MSYS/"))
                        {
                            int idx = line.IndexOf("MSYS/");
                            double[] v = ParseDoubles(line.Substring(idx + 5));
                            if (v != null && v.Length >= 9)
                            {
                                mcsOrigin = new double[] { v[0], v[1], v[2] };
                                mcsX = Normalize(new double[] { v[3], v[4], v[5] });
                                mcsY = Normalize(new double[] { v[6], v[7], v[8] });
                                mcsZ = Cross(mcsX, mcsY);
                            }
                        }

                        // GOTO/ X,Y,Z[,I,J,K] — posição da ferramenta em coordenadas MCS
                        if (line.Contains("GOTO/"))
                        {
                            int gotoIdx = line.IndexOf("GOTO/");
                            double[] v = ParseDoubles(line.Substring(gotoIdx + 5));
                            if (v != null && v.Length >= 3)
                            {
                                // Transforma ponto do MCS para ACS:
                                // P_acs = origin + x*mcsX + y*mcsY + z*mcsZ
                                double px = mcsOrigin[0] + v[0] * mcsX[0] + v[1] * mcsY[0] + v[2] * mcsZ[0];
                                double py = mcsOrigin[1] + v[0] * mcsX[1] + v[1] * mcsY[1] + v[2] * mcsZ[1];
                                double pz = mcsOrigin[2] + v[0] * mcsX[2] + v[1] * mcsY[2] + v[2] * mcsZ[2];
                                pointsList.Add(new double[] { px, py, pz });
                            }
                        }
                    }
                }
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }

            if (pointsList.Count == 0)
                throw new Exception(
                    "Não foi possível extrair os pontos da trajetória.\n" +
                    "Verifique se a operação já foi calculada no NX (botão 'Gerar' na operação antes de rodar o plugin).");

            return pointsList.ToArray();
        }

        // Converte uma string de valores separados por vírgula em array de doubles
        private static double[] ParseDoubles(string s)
        {
            string[] parts = s.Trim().Split(',');
            List<double> vals = new List<double>();
            foreach (string p in parts)
            {
                double d;
                if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    vals.Add(d);
            }
            return vals.Count > 0 ? vals.ToArray() : null;
        }

        private static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (len < 1e-10) return new double[] { 1, 0, 0 };
            return new double[] { v[0] / len, v[1] / len, v[2] / len };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new double[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }




        // ISOLAMENTO DO GRÁFICO: Isso impede que a falta da DLL trave o resto do código
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void MostrarGraficoSeguro(double[] erros, double tolerancia, ListingWindow lw)
        {
            try
            {
                lw.WriteLine("\n[Gráfico] Abrindo gráfico analítico de flexão na janela...");
                DeflectionChartVisualizer chartVisualizer = new DeflectionChartVisualizer(erros, tolerancia);
                chartVisualizer.ShowChart();
            }
            catch (Exception ex)
            {
                lw.WriteLine($"\n[ERRO NO GRÁFICO] Não foi possível exibir a janela 2D.");
                lw.WriteLine($"Motivo: {ex.Message}");
                lw.WriteLine($"SOLUÇÃO: Garanta que o arquivo 'System.Windows.Forms.DataVisualization.dll' está na mesma pasta da sua DLL do plugin.");
            }
        }

        public void Run()
        {
            ListingWindow lw = theSession.ListingWindow;
            lw.Open();

            try
            {
                Operation activeOp = GetSelectedOperation();
                lw.WriteLine($"Operação selecionada: {activeOp.Name}");

                JanelaParametros janela = new JanelaParametros();
                
                if (janela.ShowDialog() == DialogResult.OK)
                {
                    string material = janela.MaterialSelecionado;
                    double kcMaterial = janela.KcMaterial;
                    double ap = janela.Ap;  
                    double ae = janela.Ae;  
                    double fz = janela.Fz;  
                    double diametroReal = janela.Diametro;
                    double balancoReal = janela.Balanco;
                    double tolerancia = janela.Tolerancia;

                    lw.WriteLine($"Peça: {material} | Kc: {kcMaterial:F0} N/mm² | Ap: {ap} mm | Ae: {ae} mm | Fz: {fz} mm/dente");
                    lw.WriteLine($"Fresa: Ø{diametroReal} mm | Balanço (L): {balancoReal} mm");
                    lw.WriteLine($"Tolerância Requerida: {tolerancia} mm");

                    physicsCalc.CalculateToolProperties(diametroReal, out double E, out double I);
                    double[][] realPath = ExtractRealPathPoints(activeOp);

                    PontoAnaliseDeflexao[] analise = physicsCalc.CalculateCuttingAnalysis(
                        realPath, kcMaterial, ap, ae, fz, diametroReal, balancoReal, E, I);

                    // Extrai vetores simples para o visualizador de gráfico e heatmap
                    double[] errosGeometricos = new double[analise.Length];
                    double[] forcasReais      = new double[analise.Length];
                    for (int i = 0; i < analise.Length; i++)
                    {
                        errosGeometricos[i] = analise[i].DeflexaoMediaTotal;
                        forcasReais[i]      = analise[i].ForcaPicoMax;
                    }

                    double maxErro = 0;
                    double maxForca = 0;
                    int indiceDoPico = 0;
                    
                    for (int i = 0; i < errosGeometricos.Length; i++) 
                    {
                        if (errosGeometricos[i] > maxErro) 
                        {
                            maxErro = errosGeometricos[i];
                            maxForca = forcasReais[i];
                            indiceDoPico = i;
                        }
                    }

                    double[] piorPonto = realPath[indiceDoPico];

                    lw.WriteLine("\n--- Análise Dinâmica de Deflexão Concluída ---");
                    lw.WriteLine($"Pico de Força de Corte Detectado: {maxForca:F2} N");
                    lw.WriteLine($"Erro Máximo de Flexão (Médio): {maxErro:F4} mm");
                    lw.WriteLine($"Deflexão de Pico (piora crit.): {analise[indiceDoPico].DeflexaoPico:F4} mm");
                    lw.WriteLine($"Tensão de Flexão Máxima: {analise[indiceDoPico].TensaoFlexaoMaxMPa:F1} MPa");
                    lw.WriteLine($"Amplitude de Oscilação (p-a-p): {analise[indiceDoPico].AmplitudeOscilacao:F4} mm");
                    
                    if (maxErro > tolerancia)
                    {
                        lw.WriteLine($"*** ALERTA: O erro máximo ultrapassou a tolerância de {tolerancia} mm! ***");
                    }

                    lw.WriteLine($"Localização do Pico (X, Y, Z): X{piorPonto[0]:F2} Y{piorPonto[1]:F2} Z{piorPonto[2]:F2}");

                    HeatmapVisualizer heatmap = new HeatmapVisualizer();
                    heatmap.PlotHeatmap3D(realPath, errosGeometricos, tolerancia, out int qtdVerde, out int qtdAmarelo, out int qtdVermelho);
                    
                    int movimentosSeguros = realPath.Length - (qtdVerde + qtdAmarelo + qtdVermelho);
                    
                    lw.WriteLine("\n--- Legenda do Mapa de Calor 3D ---");
                    lw.WriteLine($"Total de coordenadas processadas na usinagem: {realPath.Length}");
                    lw.WriteLine($"[Vermelho] Crítico (>= {tolerancia:F3} mm): {qtdVermelho} marcações");
                    lw.WriteLine($"[Amarelo]  Alerta  (>= {(tolerancia * 0.60):F3} mm): {qtdAmarelo} marcações");
                    lw.WriteLine($"[Verde]    Visível (>= {(tolerancia * 0.20):F3} mm): {qtdVerde} marcações");
                    lw.WriteLine($"* Zonas transparentes: {movimentosSeguros} coordenadas estão dentro da margem segura e não foram coloridas.");

                    // Chamada segura do Gráfico!
                    MostrarGraficoSeguro(errosGeometricos, tolerancia, lw);
                }
                else
                {
                    lw.WriteLine("Ação cancelada pelo usuário (Janela fechada).");
                }
            }
            catch (Exception ex)
            {
                lw.WriteLine("Erro na execução: " + ex.Message);
            }
        }

        public static int Main(string[] args)
        {
            ToolDeflectionAnalyzer analyzer = new ToolDeflectionAnalyzer();
            analyzer.Run();
            return 0;
        }
        
        public static int GetUnloadOption(string dummy) { return (int)Session.LibraryUnloadOption.Immediately; }
    }
}