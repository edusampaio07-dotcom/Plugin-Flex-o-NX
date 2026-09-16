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
                    foreach (string line in lines)
                    {
                        if (line.Contains("GOTO/"))
                        {
                            string[] parts = line.Split(new string[] { "GOTO/", "," }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                double x = Convert.ToDouble(parts[0].Trim(), CultureInfo.InvariantCulture);
                                double y = Convert.ToDouble(parts[1].Trim(), CultureInfo.InvariantCulture);
                                double z = Convert.ToDouble(parts[2].Trim(), CultureInfo.InvariantCulture);
                                pointsList.Add(new double[] { x, y, z });
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
                throw new Exception("Não foi possível extrair os pontos. Verifique se a operação no NX já tem a trajetória calculada.");

            return pointsList.ToArray();
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
                    double ap = janela.Ap;  
                    double ae = janela.Ae;  
                    double fz = janela.Fz;  
                    double diametroReal = janela.Diametro;
                    double balancoReal = janela.Balanco;
                    double tolerancia = janela.Tolerancia;

                    lw.WriteLine($"Peça: {material} | Ap: {ap} mm | Ae: {ae} mm | Fz: {fz} mm/dente");
                    lw.WriteLine($"Fresa: Ø{diametroReal} mm | Balanço (L): {balancoReal} mm");
                    lw.WriteLine($"Tolerância Requerida: {tolerancia} mm");

                    physicsCalc.CalculateToolProperties(diametroReal, out double E, out double I);
                    double[][] realPath = ExtractRealPathPoints(activeOp);

                    double[] forcasReais = physicsCalc.CalculateCuttingForce(realPath, material, ap, ae, fz, diametroReal);
                    double[] errosGeometricos = physicsCalc.CalculateDeflection(realPath, forcasReais, balancoReal, E, I);

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
                    lw.WriteLine($"Erro Máximo de Flexão (Pico): {maxErro:F4} mm");
                    
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