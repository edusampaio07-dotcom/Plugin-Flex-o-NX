using System;
using NXOpen;

namespace NXDeflectionPlugin
{
    public class HeatmapVisualizer
    {
        private Session theSession;

        public HeatmapVisualizer()
        {
            theSession = Session.GetSession();
        }

        // Adicionamos os parâmetros "out" para exportar as contagens individuais
        public void PlotHeatmap3D(double[][] path, double[] deflections, double toleranciaMaxima, out int qtdVerde, out int qtdAmarelo, out int qtdVermelho)
        {
            Part workPart = theSession.Parts.Work;
            DisplayModification dispMod = theSession.DisplayManager.NewDisplayModification();
            
            qtdVerde = 0;
            qtdAmarelo = 0;
            qtdVermelho = 0;

            double limiteCritico = toleranciaMaxima;
            double limiteAlerta = toleranciaMaxima * 0.60;
            double limiteObservavel = toleranciaMaxima * 0.20;

            Session.UndoMarkId markId = theSession.SetUndoMark(Session.MarkVisibility.Visible, "Gerar Nuvem de Pontos");

            for (int i = 0; i < path.Length; i++)
            {
                double defl = deflections[i];
                
                if (defl < limiteObservavel) continue;

                Point3d p3d = new Point3d(path[i][0], path[i][1], path[i][2]);
                Point nxPoint = workPart.Points.CreatePoint(p3d);
                
                nxPoint.SetVisibility(SmartObject.VisibilityOption.Visible);

                // Aplica cores e soma no contador específico
                if (defl >= limiteCritico)
                {
                    dispMod.NewColor = 186; // Vermelho
                    dispMod.NewWidth = DisplayableObject.ObjectWidth.Thick;
                    qtdVermelho++;
                }
                else if (defl >= limiteAlerta)
                {
                    dispMod.NewColor = 46; // Amarelo
                    dispMod.NewWidth = DisplayableObject.ObjectWidth.Normal;
                    qtdAmarelo++;
                }
                else
                {
                    dispMod.NewColor = 36; // Verde
                    dispMod.NewWidth = DisplayableObject.ObjectWidth.Thin;
                    qtdVerde++;
                }
                
                dispMod.Apply(new DisplayableObject[] { nxPoint });
            }
            
            dispMod.Dispose();
            
            theSession.UpdateManager.DoUpdate(markId);
            
            if (workPart.Views.WorkView != null)
            {
                workPart.Views.WorkView.Regenerate();
            }
        }
    }
}