 using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace NXDeflectionPlugin
{
    public class DeflectionChartVisualizer : Form
    {
        public DeflectionChartVisualizer(double[] deflections, double tolerancia)
        {
            this.Text = "Relatório Dinâmico de Flexão";
            this.Size = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            Chart chart = new Chart();
            chart.Dock = DockStyle.Fill;
            
            ChartArea chartArea = new ChartArea("MainArea");
            chartArea.AxisX.Title = "Passos da Trajetória (Pontos)";
            chartArea.AxisY.Title = "Deflexão (mm)";
            chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;
            chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
            chart.ChartAreas.Add(chartArea);

            // Série principal: A curva de flexão da ferramenta
            Series seriesFlexao = new Series("Flexão Real");
            seriesFlexao.ChartType = SeriesChartType.Line;
            seriesFlexao.Color = Color.Blue;
            seriesFlexao.BorderWidth = 2;

            // Série secundária: A linha de limite da tolerância (Reta vermelha)
            Series seriesTolerancia = new Series("Tolerância Máxima");
            seriesTolerancia.ChartType = SeriesChartType.Line;
            seriesTolerancia.Color = Color.Red;
            seriesTolerancia.BorderWidth = 2;
            seriesTolerancia.BorderDashStyle = ChartDashStyle.Dash;

            for (int i = 0; i < deflections.Length; i++)
            {
                seriesFlexao.Points.AddXY(i, deflections[i]);
                seriesTolerancia.Points.AddXY(i, tolerancia);
            }

            chart.Series.Add(seriesFlexao);
            chart.Series.Add(seriesTolerancia);

            // Adiciona uma legenda
            chart.Legends.Add(new Legend("Legend1"));
            chart.Legends["Legend1"].Docking = Docking.Top;

            this.Controls.Add(chart);
        }

        public void ShowChart()
        {
            this.ShowDialog();
        }
    }
}