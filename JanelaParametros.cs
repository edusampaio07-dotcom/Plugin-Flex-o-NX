using System;
using System.Drawing;
using System.Windows.Forms;

namespace NXDeflectionPlugin
{
    public class JanelaParametros : Form
    {
        public string MaterialSelecionado { get; private set; }
        public double Ap { get; private set; }
        public double Ae { get; private set; }
        public double Fz { get; private set; }
        public double Diametro { get; private set; }
        public double Balanco { get; private set; }
        public double Tolerancia { get; private set; }

        private ComboBox cbMaterial;
        private TextBox txtAp;
        private TextBox txtAe;
        private TextBox txtFz;
        private TextBox txtDiametro;
        private TextBox txtBalanco;
        private TextBox txtTolerancia;
        private Button btnCalcular;
        
        // Novo motor de layout inteligente
        private FlowLayoutPanel painelPrincipal;

        public JanelaParametros()
        {
            this.Text = "Parâmetros de Usinagem";
            
            // Devolvemos o controle de escala para a Fonte, para respeitar o seu monitor
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));

            this.Size = new Size(420, 850); 
            this.MinimumSize = new Size(350, 600); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable; 
            this.MaximizeBox = true; 
            this.MinimizeBox = true; 

            // Configuração do Painel que empilha os itens automaticamente
            painelPrincipal = new FlowLayoutPanel();
            painelPrincipal.Dock = DockStyle.Fill;
            painelPrincipal.FlowDirection = FlowDirection.TopDown; // Ordem de cima para baixo
            painelPrincipal.WrapContents = false;
            painelPrincipal.AutoScroll = true; // Se a tela ficar pequena, ele cria barra de rolagem!
            painelPrincipal.Padding = new Padding(20, 20, 20, 20); // Margem interna
            this.Controls.Add(painelPrincipal);

            // Fonte de destaque ajustada para 12 (14 estava cortando a base da caixa de texto)
            Font fonteDestaque = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0)));

            // Usamos Padding (Margens) em vez de coordenadas para dar espaço entre os itens
            Label lblMaterial = new Label() { Text = "Material da Peça:", AutoSize = true, Margin = new Padding(0, 5, 0, 5) };
            cbMaterial = new ComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Font = fonteDestaque, Margin = new Padding(0, 0, 0, 15) };
            cbMaterial.Items.AddRange(new string[] { "Alumínio Aeronáutico", "Aço 1045", "Titânio Ti-6Al-4V", "PA-CF (Nylon com Carbono)" });
            cbMaterial.SelectedIndex = 0;

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

            btnCalcular = new Button() { 
                Text = "Calcular Deflexão", 
                Height = 50,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(0))),
                Margin = new Padding(0, 10, 0, 20)
            };
            btnCalcular.Click += new EventHandler(BtnCalcular_Click);

            // Injeta os componentes na ordem correta dentro do painel
            painelPrincipal.Controls.Add(lblMaterial);
            painelPrincipal.Controls.Add(cbMaterial);
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

            // Garante que o tamanho das caixas acompanhe a janela como um elástico
            painelPrincipal.Resize += PainelPrincipal_Resize;
            AjustarLarguras(); 
        }

        private void PainelPrincipal_Resize(object sender, EventArgs e)
        {
            AjustarLarguras();
        }

        private void AjustarLarguras()
        {
            // Pega a largura livre descontando as margens e a barra de rolagem (se existir)
            int larguraUtil = painelPrincipal.ClientSize.Width - 45; 
            if (larguraUtil < 100) larguraUtil = 100; // Trava de segurança

            cbMaterial.Width = larguraUtil;
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
                MaterialSelecionado = cbMaterial.SelectedItem.ToString();
                Ap = Convert.ToDouble(txtAp.Text);
                Ae = Convert.ToDouble(txtAe.Text);
                Fz = Convert.ToDouble(txtFz.Text);
                Diametro = Convert.ToDouble(txtDiametro.Text);
                Balanco = Convert.ToDouble(txtBalanco.Text);
                Tolerancia = Convert.ToDouble(txtTolerancia.Text);

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (FormatException)
            {
                MessageBox.Show("Por favor, insira apenas números válidos nos campos de parâmetros.", "Erro de Formatação", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}