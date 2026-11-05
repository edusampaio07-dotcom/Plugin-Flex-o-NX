using System;
using System.Collections.Generic;
using NXOpen;

namespace NXDeflectionPlugin
{
    public class HeatmapVisualizer
    {
        private Session theSession;

        // Paradas do gradiente (fração da tolerância -> RGB em níveis 0..5 da paleta padrão do NX)
        // Azul (seguro) -> Ciano -> Verde (observável) -> Amarelo (alerta) -> Vermelho (crítico)
        private static readonly double[] gradStops = { 0.00, 0.10, 0.20, 0.60, 1.00 };
        private static readonly double[,] gradRgb =
        {
            { 0, 0, 5 }, // Azul
            { 0, 5, 5 }, // Ciano
            { 0, 5, 0 }, // Verde
            { 5, 5, 0 }, // Amarelo
            { 5, 0, 0 }  // Vermelho
        };

        public HeatmapVisualizer()
        {
            theSession = Session.GetSession();
        }

        // Desenha o caminho da ferramenta como segmentos de linha coloridos em gradiente conforme a deflexão.
        // Os contadores "out" continuam contando coordenadas por faixa (verde/amarelo/vermelho).
        public void PlotHeatmap3D(double[][] path, double[] deflections, double toleranciaMaxima, out int qtdVerde, out int qtdAmarelo, out int qtdVermelho)
        {
            Part workPart = theSession.Parts.Work;

            qtdVerde = 0;
            qtdAmarelo = 0;
            qtdVermelho = 0;

            double limiteCritico = toleranciaMaxima;
            double limiteAlerta = toleranciaMaxima * 0.60;
            double limiteObservavel = toleranciaMaxima * 0.20;

            for (int i = 0; i < path.Length; i++)
            {
                double defl = deflections[i];
                if (defl >= limiteCritico) qtdVermelho++;
                else if (defl >= limiteAlerta) qtdAmarelo++;
                else if (defl >= limiteObservavel) qtdVerde++;
            }

            Session.UndoMarkId markId = theSession.SetUndoMark(Session.MarkVisibility.Visible, "Gerar Caminho de Deflexão");

            // Agrupa as linhas por (cor, espessura) para aplicar a modificação de exibição em lote
            // Chave = cor * 10 + (1 se espessa, 0 se normal)
            var grupos = new Dictionary<int, List<DisplayableObject>>();

            for (int i = 1; i < path.Length; i++)
            {
                Point3d p0 = new Point3d(path[i - 1][0], path[i - 1][1], path[i - 1][2]);
                Point3d p1 = new Point3d(path[i][0], path[i][1], path[i][2]);

                double dx = p1.X - p0.X, dy = p1.Y - p0.Y, dz = p1.Z - p0.Z;
                if (dx * dx + dy * dy + dz * dz < 1e-12) continue; // segmento degenerado

                // Usa a pior deflexão entre as extremidades do segmento (conservador)
                double defl = Math.Max(deflections[i - 1], deflections[i]);

                Line linha = workPart.Curves.CreateLine(p0, p1);

                int chave = CorGradiente(defl, toleranciaMaxima) * 10 + (defl >= limiteCritico ? 1 : 0);

                List<DisplayableObject> lista;
                if (!grupos.TryGetValue(chave, out lista))
                {
                    lista = new List<DisplayableObject>();
                    grupos[chave] = lista;
                }
                lista.Add(linha);
            }

            DisplayModification dispMod = theSession.DisplayManager.NewDisplayModification();
            dispMod.ApplyToAllFaces = false;

            foreach (var grupo in grupos)
            {
                dispMod.NewColor = grupo.Key / 10;
                dispMod.NewWidth = (grupo.Key % 10 == 1) ? DisplayableObject.ObjectWidth.Thick : DisplayableObject.ObjectWidth.Normal;
                dispMod.Apply(grupo.Value.ToArray());
            }

            dispMod.Dispose();

            theSession.UpdateManager.DoUpdate(markId);

            if (workPart.Views.WorkView != null)
            {
                workPart.Views.WorkView.Regenerate();
                workPart.Views.WorkView.Fit(); // enquadra o caminho inteiro
            }
        }

        // Desenha em magenta tracejado os trechos de arco que NÃO são percorridos pela ferramenta
        // (alternativa descartada do arco), para deixar claro que a ferramenta não passa por ali.
        public void PlotTrechosNaoPercorridos(List<double[][]> trechos)
        {
            if (trechos == null || trechos.Count == 0) return;
            Part workPart = theSession.Parts.Work;
            List<DisplayableObject> objs = new List<DisplayableObject>();

            foreach (double[][] poli in trechos)
            {
                for (int i = 1; i < poli.Length; i++)
                {
                    Point3d p0 = new Point3d(poli[i - 1][0], poli[i - 1][1], poli[i - 1][2]);
                    Point3d p1 = new Point3d(poli[i][0], poli[i][1], poli[i][2]);
                    double dx = p1.X - p0.X, dy = p1.Y - p0.Y, dz = p1.Z - p0.Z;
                    if (dx * dx + dy * dy + dz * dz < 1e-12) continue;
                    objs.Add(workPart.Curves.CreateLine(p0, p1));
                }
            }
            if (objs.Count == 0) return;

            DisplayModification dispMod = theSession.DisplayManager.NewDisplayModification();
            dispMod.ApplyToAllFaces = false;
            dispMod.NewColor = 1 + 36 * (5 - 0) + 6 * (5 - 5) + (5 - 5); // magenta (R=5,G=0,B=5) = 181
            dispMod.NewFont = DisplayableObject.ObjectFont.Dashed;
            dispMod.Apply(objs.ToArray());
            dispMod.Dispose();
        }

        // Interpola o gradiente e converte para o índice da paleta padrão do NX (cubo 6x6x6, IDs 1..216).
        private static int CorGradiente(double defl, double tolerancia)
        {
            double t = tolerancia > 0 ? defl / tolerancia : 1.0;
            if (double.IsNaN(t) || t < 0) t = 0;
            if (t > 1) t = 1;

            int k = 0;
            while (k < gradStops.Length - 2 && t > gradStops[k + 1]) k++;

            double f = (t - gradStops[k]) / (gradStops[k + 1] - gradStops[k]);
            int r = (int)Math.Round(gradRgb[k, 0] + f * (gradRgb[k + 1, 0] - gradRgb[k, 0]));
            int g = (int)Math.Round(gradRgb[k, 1] + f * (gradRgb[k + 1, 1] - gradRgb[k, 1]));
            int b = (int)Math.Round(gradRgb[k, 2] + f * (gradRgb[k + 1, 2] - gradRgb[k, 2]));

            // Paleta padrão do NX: ID = 1 + 36*(5-G) + 6*(5-R) + (5-B)
            // (ex.: 186 = vermelho, 36 = verde, 211 = azul, 6 = amarelo, 31 = ciano)
            return 1 + 36 * (5 - g) + 6 * (5 - r) + (5 - b);
        }
    }
}
