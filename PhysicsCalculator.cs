using System;
using System.Collections.Generic;

namespace NXDeflectionPlugin
{
    public class PhysicsCalculator
    {
        // 1. Método intacto das propriedades físicas da ferramenta
        public void CalculateToolProperties(double diameter, out double elasticModulus, out double momentOfInertia)
        {
            elasticModulus = 600000.0; // Metal Duro p/ fresa
            momentOfInertia = (Math.PI * Math.Pow(diameter, 4)) / 64.0;
        }

        // 2. NOVO: Cálculo de Força Dinâmico usando o Motor Volumétrico (Z-Buffer)
        public double[] CalculateCuttingForce(double[][] pathPoints, string material, double apTeorico, double aeTeorico, double fz, double diameter)
        {
            int numPoints = pathPoints.Length;
            double[] forces = new double[numPoints];
            double Kc = MaterialDatabase.GetKc(material);
            
            // Instancia o motor volumétrico
            VolumetricEngine engine = new VolumetricEngine();
            
            // Cria um bloco de material bruto que engloba toda a trajetória (Bounding Box automático)
            engine.InicializarBlankPorTrajetoria(pathPoints, diameter, apTeorico);

            // Resolução de avanço para a ferramenta não "teleportar" (ex: passos de 0.5 mm)
            double stepSize = 0.5;

            for (int i = 1; i < numPoints; i++)
            {
                double[] pPrev = pathPoints[i - 1];
                double[] pCurr = pathPoints[i];

                double dist = Math.Sqrt(Math.Pow(pCurr[0] - pPrev[0], 2) + Math.Pow(pCurr[1] - pPrev[1], 2));
                
                // Variáveis para guardar o maior esforço lido entre P1 e P2
                double maxAe = 0;
                double maxAp = 0;

                // Interpolação espacial do movimento
                int steps = Math.Max(1, (int)(dist / stepSize));
                for (int s = 1; s <= steps; s++)
                {
                    double fraction = (double)s / steps;
                    double[] interPoint = new double[] 
                    {
                        pPrev[0] + (pCurr[0] - pPrev[0]) * fraction,
                        pPrev[1] + (pCurr[1] - pPrev[1]) * fraction,
                        pPrev[2] + (pCurr[2] - pPrev[2]) * fraction
                    };

                    // O motor atualiza a matriz 3D e devolve o contato real naquele milissegundo
                    engine.SimularCorte(interPoint, diameter / 2.0, out double aeReal, out double apReal);

                    if (aeReal > maxAe) maxAe = aeReal;
                    if (apReal > maxAp) maxAp = apReal;
                }

                // Se o motor não detectou material (corte no vazio), a força é zero
                if (maxAe <= 0.01 || maxAp <= 0.01)
                {
                    forces[i] = 0;
                }
                else
                {
                    // Aplica a equação da força com o contato DINÂMICO detectado na matriz
                    double hmDin = fz * Math.Sqrt(maxAe / diameter);
                    forces[i] = Kc * maxAp * hmDin;
                }
            }
            
            // Garante que o primeiro ponto não fique vazio
            forces[0] = forces[1]; 

            return forces;
        }

        // 3. Método intacto de cálculo da viga engastada
        public double[] CalculateDeflection(double[][] pathPoints, double[] forces, double L, double E, double I)
        {
            int numPoints = forces.Length;
            double[] deflections = new double[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                deflections[i] = (forces[i] * Math.Pow(L, 3)) / (3.0 * E * I);
            }
            return deflections;
        }
    }

    // CLASSE INTERNA: O simulador volumétrico independente
    public class VolumetricEngine
    {
        private double[,] zBuffer;
        private double resolucaoMalha = 0.5; // Malha de 0.5mm para manter o PC rápido
        private int tamanhoX, tamanhoY;
        private double minX_Global, minY_Global;

        // Cria o bloco bruto baseando-se nas coordenadas máximas da usinagem
        public void InicializarBlankPorTrajetoria(double[][] points, double diametro, double apMaximo)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            double maxZ = double.MinValue;

            // Encontra os limites (Bounding Box)
            foreach (var p in points)
            {
                if (p[0] < minX) minX = p[0];
                if (p[1] < minY) minY = p[1];
                if (p[0] > maxX) maxX = p[0];
                if (p[1] > maxY) maxY = p[1];
                if (p[2] > maxZ) maxZ = p[2];
            }

            // Adiciona uma margem de segurança do tamanho da fresa
            minX -= diametro; minY -= diametro;
            maxX += diametro; maxY += diametro;

            minX_Global = minX;
            minY_Global = minY;

            tamanhoX = (int)((maxX - minX) / resolucaoMalha) + 1;
            tamanhoY = (int)((maxY - minY) / resolucaoMalha) + 1;
            
            zBuffer = new double[tamanhoX, tamanhoY];

            // Assume que a altura inicial do material é o ponto mais alto da trajetória + o Ap teórico
            double alturaMaterial = maxZ + apMaximo;

            for (int i = 0; i < tamanhoX; i++)
            {
                for (int j = 0; j < tamanhoY; j++)
                {
                    zBuffer[i, j] = alturaMaterial;
                }
            }
        }

        // Subtrai o material e calcula os engajamentos
        public void SimularCorte(double[] pontoAtual, double raioFerramenta, out double aeReal, out double apReal)
        {
            int pontosCortados = 0;
            double volumeRemovido = 0;

            // Converte a coordenada real (mm) para o índice da matriz [i, j]
            int centroI = (int)((pontoAtual[0] - minX_Global) / resolucaoMalha);
            int centroJ = (int)((pontoAtual[1] - minY_Global) / resolucaoMalha);
            int raioEmIndices = (int)(raioFerramenta / resolucaoMalha);

            // Otimização: Varre apenas o quadrado (Bounding Box) da ferramenta
            int minI = Math.Max(0, centroI - raioEmIndices);
            int maxI = Math.Min(tamanhoX - 1, centroI + raioEmIndices);
            int minJ = Math.Max(0, centroJ - raioEmIndices);
            int maxJ = Math.Min(tamanhoY - 1, centroJ + raioEmIndices);

            double zFerramenta = pontoAtual[2];

            for (int i = minI; i <= maxI; i++)
            {
                for (int j = minJ; j <= maxJ; j++)
                {
                    // Verifica se o ponto da matriz está dentro do círculo da fresa
                    double distQuadrado = Math.Pow(i - centroI, 2) + Math.Pow(j - centroJ, 2);
                    
                    if (distQuadrado <= Math.Pow(raioEmIndices, 2))
                    {
                        // Se o prego virtual for mais alto que a ferramenta, ocorre o corte
                        if (zFerramenta < zBuffer[i, j])
                        {
                            volumeRemovido += (zBuffer[i, j] - zFerramenta) * Math.Pow(resolucaoMalha, 2);
                            zBuffer[i, j] = zFerramenta; // Afunda o material
                            pontosCortados++;
                        }
                    }
                }
            }

            // Estimativa geométrica:
            // Se metade da área do círculo foi cortada, o Ae = Raio da ferramenta.
            double areaFerramenta = Math.PI * Math.Pow(raioFerramenta, 2);
            double areaCortada = pontosCortados * Math.Pow(resolucaoMalha, 2);
            
            // Razão de engajamento (AreaCortada / AreaTotal), escalada para o Diâmetro (2 * Raio)
            double taxaDeContatoRadial = areaCortada / areaFerramenta;
            aeReal = Math.Min(raioFerramenta * 2.0, taxaDeContatoRadial * (raioFerramenta * 2.0));

            // Profundidade média (Ap)
            apReal = (areaCortada > 0) ? volumeRemovido / areaCortada : 0;
        }
    }
}