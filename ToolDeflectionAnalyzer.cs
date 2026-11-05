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

        // Trechos de arco que o CLS permitiria (sentido do eixo) mas que NÃO são percorridos:
        // o arco real é o menor (<= 180°); o complementar é guardado só para exibição em outra cor.
        public List<double[][]> TrechosNaoPercorridos = new List<double[][]>(); 

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
            TrechosNaoPercorridos = new List<double[][]>();
            string tempFile = Path.Combine(Path.GetTempPath(), "toolpath_temp.cls");

            try
            {
                CAMSetup camSetup = theSession.Parts.Work.CAMSetup;
                camSetup.OutputClsf(new CAMObject[] { op }, "CLSF_STANDARD", tempFile, CAMSetup.OutputUnits.Metric);

                if (File.Exists(tempFile))
                {
                    string[] lines = JuntarContinuacoes(File.ReadAllLines(tempFile));

                    // Transformação MCS → ACS: identidade por padrão
                    // (caso o arquivo não tenha MSYS/, os pontos ficam no MCS original)
                    double[] mcsOrigin = new double[] { 0, 0, 0 };
                    double[] mcsX = new double[] { 1, 0, 0 }; // eixo X do MCS em ACS
                    double[] mcsY = new double[] { 0, 1, 0 }; // eixo Y do MCS em ACS
                    double[] mcsZ = new double[] { 0, 0, 1 }; // eixo Z = X × Y
                    double[] circ = null;     // último registro CIRCLE/ pendente (aplica ao próximo GOTO/)
                    double[] lastMcs = null;  // último ponto em coordenadas MCS

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
                        // CIRCLE/ xc,yc,zc, i,j,k, raio, ... — movimento circular até o próximo GOTO/
                        if (line.TrimStart().StartsWith("CIRCLE/"))
                        {
                            circ = ParseDoubles(line.Substring(line.IndexOf("CIRCLE/") + 7));
                        }
                        else if (line.TrimStart().StartsWith("FROM/"))
                        {
                            double[] f = ParseDoubles(line.Substring(line.IndexOf("FROM/") + 5));
                            if (f != null && f.Length >= 3) lastMcs = new double[] { f[0], f[1], f[2] };
                        }
                        else if (line.Contains("GOTO/"))
                        {
                            int gotoIdx = line.IndexOf("GOTO/");
                            double[] v = ParseDoubles(line.Substring(gotoIdx + 5));
                            List<double[]> segs = new List<double[]>();
                            if (v != null && v.Length >= 3)
                            {
                                if (circ != null && circ.Length >= 7 && lastMcs != null)
                                    {
                                    List<double[]> comp;
                                    segs.AddRange(ArcPoints(lastMcs, v, circ, out comp));
                                    if (comp != null && comp.Count > 1)
                                    {
                                        List<double[]> g = new List<double[]>();
                                        foreach (double[] q in comp) g.Add(ToAcs(q, mcsOrigin, mcsX, mcsY, mcsZ));
                                        TrechosNaoPercorridos.Add(g.ToArray());
                                    }
                                }
                                segs.Add(new double[] { v[0], v[1], v[2] });
                                lastMcs = segs[segs.Count - 1];
                            }
                            circ = null;
                            foreach (double[] m in segs)
                            {
                                // Transforma ponto do MCS para ACS:
                                // P_acs = origin + x*mcsX + y*mcsY + z*mcsZ
                                double px = mcsOrigin[0] + m[0] * mcsX[0] + m[1] * mcsY[0] + m[2] * mcsZ[0];
                                double py = mcsOrigin[1] + m[0] * mcsX[1] + m[1] * mcsY[1] + m[2] * mcsZ[1];
                                double pz = mcsOrigin[2] + m[0] * mcsX[2] + m[1] * mcsY[2] + m[2] * mcsZ[2];
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

        // Discretiza um arco (CIRCLE/ xc,yc,zc,i,j,k,raio) de 'start' até 'end' (sentido anti-horário
        // em torno do eixo ijk, regra da mão direita). Retorna os pontos intermediários (sem o final).
        private static List<double[]> ArcPoints(double[] start, double[] end, double[] circ, out List<double[]> complementar)
        {
            List<double[]> pts = new List<double[]>();
            complementar = null;
            double[] c = new double[] { circ[0], circ[1], circ[2] };
            double[] w = Normalize(new double[] { circ[3], circ[4], circ[5] });

            double[] s = new double[] { start[0] - c[0], start[1] - c[1], start[2] - c[2] };
            double[] e = new double[] { end[0] - c[0], end[1] - c[1], end[2] - c[2] };
            double hs = s[0] * w[0] + s[1] * w[1] + s[2] * w[2];
            double he = e[0] * w[0] + e[1] * w[1] + e[2] * w[2];
            double[] sp = new double[] { s[0] - hs * w[0], s[1] - hs * w[1], s[2] - hs * w[2] };
            double[] ep = new double[] { e[0] - he * w[0], e[1] - he * w[1], e[2] - he * w[2] };
            double rs = Math.Sqrt(sp[0] * sp[0] + sp[1] * sp[1] + sp[2] * sp[2]);
            double re = Math.Sqrt(ep[0] * ep[0] + ep[1] * ep[1] + ep[2] * ep[2]);
            if (rs < 1e-9) return pts;

            double[] u = new double[] { sp[0] / rs, sp[1] / rs, sp[2] / rs };
            double[] vv = Cross(w, u);
            double sweep = Math.Atan2(ep[0] * vv[0] + ep[1] * vv[1] + ep[2] * vv[2],
                                      ep[0] * u[0] + ep[1] * u[1] + ep[2] * u[2]);
            if (sweep <= 1e-6) sweep += 2.0 * Math.PI; // CCW; ângulo ~0 = círculo completo

            // Arco > 180°: o sentido dado pelo eixo é suspeito (a ferramenta raramente percorre mais que
            // meia volta num único arco). Usa-se o arco curto (<= 180°, sentido inverso) como trajetória real
            // e o arco longo vira "trecho não percorrido", só para exibição.
            if (sweep > Math.PI + 1e-3 && sweep < 2.0 * Math.PI - 1e-3)
            {
                complementar = GerarArco(start, end, c, w, u, vv, rs, re, hs, he, sweep);
                sweep -= 2.0 * Math.PI; // negativo = sentido horário em torno de w
            }

            double rMed = Math.Max(rs, re);
            int n = Math.Max(4, (int)Math.Ceiling(Math.Max(Math.Abs(sweep) * rMed / 0.5, Math.Abs(sweep) / (5.0 * Math.PI / 180.0))));
            for (int k = 1; k < n; k++)
            {
                double t = (double)k / n;
                double ang = sweep * t;
                double rad = rs + (re - rs) * t;
                double h = hs + (he - hs) * t; // hélice: interpola ao longo do eixo
                double ca = Math.Cos(ang) * rad, sa = Math.Sin(ang) * rad;
                pts.Add(new double[]
                {
                    c[0] + w[0] * h + u[0] * ca + vv[0] * sa,
                    c[1] + w[1] * h + u[1] * ca + vv[1] * sa,
                    c[2] + w[2] * h + u[2] * ca + vv[2] * sa
                });
            }
            return pts;
        }

        // Polilinha completa (início, intermediários, fim) de um arco com varredura 'sweep' (rad, com sinal).
        private static List<double[]> GerarArco(double[] start, double[] end, double[] c, double[] w, double[] u, double[] vv,
                                                double rs, double re, double hs, double he, double sweep)
        {
            List<double[]> pts = new List<double[]>();
            pts.Add(start);
            int n = Math.Max(4, (int)Math.Ceiling(Math.Max(Math.Abs(sweep) * Math.Max(rs, re) / 0.5, Math.Abs(sweep) / (5.0 * Math.PI / 180.0))));
            for (int k = 1; k < n; k++)
            {
                double t = (double)k / n;
                double ang = sweep * t;
                double rad = rs + (re - rs) * t;
                double h = hs + (he - hs) * t;
                double ca = Math.Cos(ang) * rad, sa = Math.Sin(ang) * rad;
                pts.Add(new double[]
                {
                    c[0] + w[0] * h + u[0] * ca + vv[0] * sa,
                    c[1] + w[1] * h + u[1] * ca + vv[1] * sa,
                    c[2] + w[2] * h + u[2] * ca + vv[2] * sa
                });
            }
            pts.Add(end);
            return pts;
        }

        // MCS -> ACS: P_acs = origem + x*X + y*Y + z*Z
        private static double[] ToAcs(double[] m, double[] o, double[] X, double[] Y, double[] Z)
        {
            return new double[]
            {
                o[0] + m[0] * X[0] + m[1] * Y[0] + m[2] * Z[0],
                o[1] + m[0] * X[1] + m[1] * Y[1] + m[2] * Z[1],
                o[2] + m[0] * X[2] + m[1] * Y[2] + m[2] * Z[2]
            };
        }

        // Junta linhas de continuação do CLS (terminadas em '$' simples), para não perder registros longos.
        private static string[] JuntarContinuacoes(string[] linhas)
        {
            List<string> res = new List<string>();
            string acc = null;
            foreach (string l in linhas)
            {
                string cur = acc == null ? l : acc + l.TrimStart();
                string tr = cur.TrimEnd();
                if (tr.EndsWith("$") && !tr.Contains("$$"))
                {
                    acc = tr.Substring(0, tr.Length - 1);
                }
                else
                {
                    res.Add(cur);
                    acc = null;
                }
            }
            if (acc != null) res.Add(acc);
            return res.ToArray();
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
                    heatmap.PlotTrechosNaoPercorridos(TrechosNaoPercorridos);
                    if (TrechosNaoPercorridos.Count > 0)
                        lw.WriteLine($"[Magenta tracejado] {TrechosNaoPercorridos.Count} trecho(s) de arco NÃO percorridos (alternativa descartada do arco; a ferramenta usa o arco curto).");
                    
                    int movimentosSeguros = realPath.Length - (qtdVerde + qtdAmarelo + qtdVermelho);
                    
                    lw.WriteLine("\n--- Legenda do Mapa de Calor 3D ---");
                    lw.WriteLine($"Total de coordenadas processadas na usinagem: {realPath.Length}");
                    lw.WriteLine($"[Vermelho] Crítico (>= {tolerancia:F3} mm): {qtdVermelho} marcações");
                    lw.WriteLine($"[Amarelo]  Alerta  (>= {(tolerancia * 0.60):F3} mm): {qtdAmarelo} marcações");
                    lw.WriteLine($"[Verde]    Visível (>= {(tolerancia * 0.20):F3} mm): {qtdVerde} marcações");
                    lw.WriteLine($"[Azul]     Seguro  (<  {(tolerancia * 0.20):F3} mm): {movimentosSeguros} coordenadas");
                    lw.WriteLine("* O caminho da ferramenta é desenhado com gradiente contínuo: Azul -> Ciano -> Verde -> Amarelo -> Vermelho.");
                    lw.WriteLine("* Segmentos críticos (>= tolerância) são exibidos com linha espessa.");

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