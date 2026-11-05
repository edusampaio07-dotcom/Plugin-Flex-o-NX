using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NXDeflectionPlugin
{
    public class JanelaParametros : Form
    {
        public string MaterialSelecionado { get; private set; }
        /// <summary>Força específica de corte Kc1.1 (N/mm²) inserida pelo usuário.</summary>
        public double KcMaterial { get; private set; }
        public double Ap { get; private set; }
        public double Ae { get; private set; }
        public double Fz { get; private set; }
        public double Diametro { get; private set; }
        public double Balanco { get; private set; }
        public double Tolerancia { get; private set; }

        private ComboBox cbPresets;
        private TextBox txtNomeMaterial;
        private TextBox txtKc;
        private TextBox txtAp;
        private TextBox txtAe;
        private TextBox txtFz;
        private TextBox txtDiametro;
        private TextBox txtBalanco;
        private TextBox txtTolerancia;
        private Button btnCalcular;
        private FlowLayoutPanel painelPrincipal;

        // Presets de materiais: nome → Kc (N/mm²)
        private static readonly Dictionary<string, double> Presets = new Dictionary<string, double>
        {
            { "Alumínio Aeronáutico (Al 7075)",  700.0  },
            { "Aço 1045",                        1500.0 },
            { "Titânio Ti-6Al-4V",               2200.0 },
            { "PA-CF (Nylon com Carbono)",        150.0  },
            { "Aço Inox 316L",                   1800.0 },
            { "Latão CW614N",                    900.0  },
        };

        public JanelaParametros()
        {
            this.Text = "Parâmetros de Usinagem";
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            this.Size = new Size(420, 980);
            this.MinimumSize = new Size(350, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;

            painelPrincipal = new FlowLayoutPanel();
            painelPrincipal.Dock = DockStyle.Fill;
            painelPrincipal.FlowDirection = FlowDirection.TopDown;
            painelPrincipal.WrapContents = false;
            painelPrincipal.AutoScroll = true;
            painelPrincipal.Padding = new Padding(20, 20, 20, 20);
            this.Controls.Add(painelPrincipal);

            Font fonteDestaque = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));

            // --- Seção: Material (preset + campos livres) ---
            Label lblPreset = new Label() { Text = "Material (preset rápido):", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            cbPresets = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Font = fonteDestaque, Margin = new Padding(0, 0, 0, 8) };
            cbPresets.Items.Add("— selecione para preencher automaticamente —");
            foreach (var kv in Presets) cbPresets.Items.Add(kv.Key);
            cbPresets.SelectedIndex = 0;
            cbPresets.SelectedIndexChanged += CbPresets_SelectedIndexChanged;

            Label lblNomeMaterial = new Label() { Text = "Nome do Material (livre):", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtNomeMaterial = new TextBox() { Text = "Alumínio Aeronáutico (Al 7075)", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 8) };

            Label lblKc = new Label() { Text = "Kc1.1 – Força Específica de Corte [N/mm²]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            Label lblKcDica = new Label()
            {
                Text = "  ↑ Edite diretamente para usar um Kc customizado.",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                Margin = new Padding(0, 0, 0, 4)
            };
            txtKc = new TextBox() { Text = "700", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            // --- Parâmetros de corte ---
            Label lblAp = new Label() { Text = "Ap (Prof. Axial) [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtAp = new TextBox() { Text = "5,0", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            Label lblAe = new Label() { Text = "Ae (Prof. Radial) [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtAe = new TextBox() { Text = "2,0", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            Label lblFz = new Label() { Text = "Fz (Avanço/Dente) [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtFz = new TextBox() { Text = "0,1", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            Label lblDiametro = new Label() { Text = "Diâmetro da Fresa (D) [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtDiametro = new TextBox() { Text = "10,0", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            Label lblBalanco = new Label() { Text = "Balanço p/ fora do mandril (L) [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtBalanco = new TextBox() { Text = "40,0", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };

            Label lblTolerancia = new Label() { Text = "Tolerância Máxima de Flexão [mm]:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            txtTolerancia = new TextBox() { Text = "0,05", Font = fonteDestaque, Margin = new Padding(0, 0, 0, 25) };

            btnCalcular = new Button()
            {
                Text = "Calcular Deflexão",
                Height = 50,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0))),
                Margin = new Padding(0, 10, 0, 20)
            };
            btnCalcular.Click += new EventHandler(BtnCalcular_Click);

            // Adiciona controles na ordem
            painelPrincipal.Controls.Add(lblPreset);
            painelPrincipal.Controls.Add(cbPresets);
            painelPrincipal.Controls.Add(lblNomeMaterial);
            painelPrincipal.Controls.Add(txtNomeMaterial);
            painelPrincipal.Controls.Add(lblKc);
            painelPrincipal.Controls.Add(lblKcDica);
            painelPrincipal.Controls.Add(txtKc);
            painelPrincipal.Controls.Add(lblAp);
            painelPrincipal.Controls.Add(txtAp);
            painelPrincipal.Controls.Add(lblAe);
            painelPrincipal.Controls.Add(txtAe);
            painelPrincipal.Controls.Add(lblFz);
            painelPrincipal.Controls.Add(txtFz);
            painelPrincipal.Controls.Add(lblDiametro);
            painelPrincipal.Controls.Add(txtDiametro);
            painelPrincipal.Controls.Add(lblBalanco);
            painelPrincipal.Controls.Add(txtBalanco);
            painelPrincipal.Controls.Add(lblTolerancia);
            painelPrincipal.Controls.Add(txtTolerancia);
            painelPrincipal.Controls.Add(btnCalcular);

            painelPrincipal.Resize += PainelPrincipal_Resize;
            AjustarLarguras();
        }

        private void CbPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selected = cbPresets.SelectedItem?.ToString() ?? "";
            if (Presets.ContainsKey(selected))
            {
                txtNomeMaterial.Text = selected;
                txtKc.Text = Presets[selected].ToString("F0", CultureInfo.CurrentCulture);
            }
        }

        private void PainelPrincipal_Resize(object sender, EventArgs e)
        {
            AjustarLarguras();
        }

        private void AjustarLarguras()
        {
            int larguraUtil = painelPrincipal.ClientSize.Width - 45;
            if (larguraUtil < 100) larguraUtil = 100;

            cbPresets.Width = larguraUtil;
            txtNomeMaterial.Width = larguraUtil;
            txtKc.Width = larguraUtil;
            txtAp.Width = larguraUtil;
            txtAe.Width = larguraUtil;
            txtFz.Width = larguraUtil;
            txtDiametro.Width = larguraUtil;
            txtBalanco.Width = larguraUtil;
            txtTolerancia.Width = larguraUtil;
            btnCalcular.Width = larguraUtil;
        }

        private void BtnCalcular_Click(object sender, EventArgs e)
        {
            try
            {
                string nomeMat = txtNomeMaterial.Text.Trim();
                if (string.IsNullOrEmpty(nomeMat))
                    throw new FormatException("O nome do material não pode ser vazio.");

                double kc = Convert.ToDouble(txtKc.Text, CultureInfo.CurrentCulture);
                if (kc <= 0)
                    throw new FormatException("Kc deve ser um valor positivo.");

                MaterialSelecionado = nomeMat;
                KcMaterial = kc;
                Ap = Convert.ToDouble(txtAp.Text);
                Ae = Convert.ToDouble(txtAe.Text);
                Fz = Convert.ToDouble(txtFz.Text);
                Diametro = Convert.ToDouble(txtDiametro.Text);
                Balanco = Convert.ToDouble(txtBalanco.Text);
                Tolerancia = Convert.ToDouble(txtTolerancia.Text);

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (FormatException fex)
            {
                MessageBox.Show("Por favor, verifique os campos: " + fex.Message,
                    "Erro de Formatação", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
