using System;
using System.Collections.Generic;

namespace NXDeflectionPlugin
{
    public enum TipoFresamento
    {
        Concordante,
        Discordante
    }

    // Estrutura com os resultados completos das 3 frentes para cada ponto da trajetória
    public struct PontoAnaliseDeflexao
    {
        // Frente 1: Tolerância e Dimensão da Parede
        public double DeflexaoParedeY;     // mm
        public double DeflexaoAvancoX;     // mm
        public double DeflexaoMediaTotal;  // mm

        // Frente 2: Quebra e Tensão Limite
        public double ForcaPicoMax;        // N
        public double DeflexaoPico;        // mm
        public double TensaoFlexaoMaxMPa;  // MPa (N/mm²)

        // Frente 3: Comportamento Dinâmico (Oscilação)
        public double AmplitudeOscilacao;  // mm (Delta_delta pico a pico)
    }

    public class PhysicsCalculator
    {
        // 1. Método de cálculo das propriedades estruturais da ferramenta
        // Atualizado com fator de redução do núcleo (d_ef ~ 0.8 * D)
        public void CalculateToolProperties(double diameter, out double elasticModulus, out double momentOfInertia, double fatorNucleo = 0.80)
        {
            elasticModulus = 600000.0; // Metal Duro WC (600 GPa = 600.000 MPa)
            double dEq = diameter * fatorNucleo;
            momentOfInertia = (Math.PI * Math.Pow(dEq, 4)) / 64.0;
        }

        // 2. Integração Numérica de Kienzle com Correções Angulares e Desgaste
        public void IntegrarKienzlePonto(
            double D, double ae, double ap, double fz, int z,
            double helixGraus, double gammaO, double gammaRef,
            double kc11, double mc, double kr, double vb, double cVb,
            TipoFresamento tipo, int nPassos,
            out double fxMedia, out double fyMedia, out double fxyMedia,
            out double forcaPicoMax, out double forcaMin)
        {
            if (ae <= 0.001 || ap <= 0.001)
            {
                fxMedia = fyMedia = fxyMedia = forcaPicoMax = forcaMin = 0.0;
                return;
            }

            double lambdaRad = helixGraus * Math.PI / 180.0;

            // Limites do arco de contato
            double phiIn, phiOut;
            if (ae >= D)
            {
                phiIn = 0.0;
                phiOut = Math.PI;
            }
            else if (tipo == TipoFresamento.Concordante)
            {
                phiIn = Math.Acos(Math.Min(1.0, Math.Max(-1.0, 2.0 * ae / D - 1.0)));
                phiOut = Math.PI;
            }
            else
            {
                phiIn = 0.0;
                phiOut = Math.Acos(Math.Min(1.0, Math.Max(-1.0, 1.0 - 2.0 * ae / D)));
            }

            // Fatores de correção de Kienzle
            double b = ap / Math.Cos(lambdaRad);
            double kGamma = 1.0 - (gammaO - gammaRef) / 100.0;
            double kLambda = 1.0 - (helixGraus / 100.0);
            double kVb = 1.0 + (cVb * vb);
            double kCorr = kGamma * kLambda * kVb;

            double c0 = kc11 * b * Math.Pow(fz, 1.0 - mc) * kCorr;

            // Quadratura via Regra dos Trapézios Composta
            double dPhi = (phiOut - phiIn) / nPassos;
            double somaX = 0.0;
            double somaY = 0.0;

            forcaPicoMax = 0.0;
            forcaMin = double.MaxValue;

            for (int i = 0; i <= nPassos; i++)
            {
                double phi = phiIn + i * dPhi;
                double peso = (i == 0 || i == nPassos) ? 0.5 : 1.0;

                double sinPhi = Math.Max(0.0, Math.Sin(phi));
                double cosPhi = Math.Cos(phi);

                double sinTermT = Math.Pow(sinPhi, 1.0 - mc);
                double sinTermR = Math.Pow(sinPhi, 2.0 - mc);

                // Forças instantâneas locais
                double fxInst = -c0 * (sinTermT * cosPhi + kr * sinTermR);
                double fyInst =  c0 * (sinTermR - kr * sinTermT * cosPhi);
                double fxyInst = Math.Sqrt(fxInst * fxInst + fyInst * fyInst);

                if (fxyInst > forcaPicoMax) forcaPicoMax = fxyInst;
                if (fxyInst < forcaMin) forcaMin = fxyInst;

                somaX += peso * fxInst;
                somaY += peso * fyInst;
            }

            double fatorGiro = z / (2.0 * Math.PI);
            fxMedia = fatorGiro * somaX * dPhi;
            fyMedia = fatorGiro * somaY * dPhi;
            fxyMedia = Math.Sqrt(fxMedia * fxMedia + fyMedia * fyMedia);
            
            if (forcaMin == double.MaxValue) forcaMin = 0.0;
        }

        // 3. Processamento Dinâmico acoplado ao Z-Buffer e Avaliação das 3 Frentes
        public PontoAnaliseDeflexao[] CalculateCuttingAnalysis(
            double[][] pathPoints, double kc11, double apTeorico, double aeTeorico, 
            double fz, double diameter, double balancoL, double E, double I,
            int zDentes = 4, double helixGraus = 30.0, double kr = 0.45,
            double gammaO = 6.0, double gammaRef = 6.0, double vb = 0.0,
            TipoFresamento tipo = TipoFresamento.Concordante)
        {
            int numPoints = pathPoints.Length;
            PontoAnaliseDeflexao[] resultados = new PontoAnaliseDeflexao[numPoints];
            
            double mc = 0.25; // Expoente médio de Kienzle para aços/ligas metálicas

            // Módulo de resistência à flexão da haste engastada
            double dEq = Math.Pow((64.0 * I) / Math.PI, 0.25);
            double wf = (Math.PI * Math.Pow(dEq, 3)) / 32.0;

            VolumetricEngine engine = new VolumetricEngine();
            engine.InicializarBlankPorTrajetoria(pathPoints, diameter, apTeorico);

            double stepSize = 0.5;

            for (int i = 1; i < numPoints; i++)
            {
                double[] pPrev = pathPoints[i - 1];
                double[] pCurr = pathPoints[i];

                double dist = Math.Sqrt(Math.Pow(pCurr[0] - pPrev[0], 2) + Math.Pow(pCurr[1] - pPrev[1], 2));

                double maxAe = 0;
                double maxAp = 0;

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

                    engine.SimularCorte(interPoint, diameter / 2.0, out double aeReal, out double apReal);

                    if (aeReal > maxAe) maxAe = aeReal;
                    if (apReal > maxAp) maxAp = apReal;
                }

                // Integração de Kienzle para o engajamento geométrico instantâneo
                IntegrarKienzlePonto(
                    diameter, maxAe, maxAp, fz, zDentes,
                    helixGraus, gammaO, gammaRef, kc11, mc, kr, vb, 1.0, tipo, 300,
                    out double fxMed, out double fyMed, out double fxyMed,
                    out double fPico, out double fMin
                );

                // Ponto médio da carga distribuída ao longo de ap
                double aEf = Math.Max(0.1, balancoL - (maxAp / 2.0));
                // Compliância elástica da viga engastada
                double compliancia = (Math.Pow(aEf, 2) / (6.0 * E * I)) * (3.0 * balancoL - aEf);

                // Frente 1: Erro Dimensional Médio
                resultados[i].DeflexaoParedeY = Math.Abs(fyMed) * compliancia;
                resultados[i].DeflexaoAvancoX = Math.Abs(fxMed) * compliancia;
                resultados[i].DeflexaoMediaTotal = fxyMed * compliancia;

                // Frente 2: Resistência Mecânica / Quebra
                resultados[i].ForcaPicoMax = fPico;
                resultados[i].DeflexaoPico = fPico * compliancia;
                resultados[i].TensaoFlexaoMaxMPa = (fPico * aEf) / wf;

                // Frente 3: Amplitude de Vibração (Pico a Pico)
                double deflexaoMin = fMin * compliancia;
                resultados[i].AmplitudeOscilacao = resultados[i].DeflexaoPico - deflexaoMin;
            }

            resultados[0] = resultados[1];
            return resultados;
        }

        // Método de compatibilidade retroativa — usa o modelo correto de viga engastada
        // com carga pontual aplicada a uma distância 'a' do engaste:
        //   δ = F * a² * (3L - a) / (6 * E * I)
        // onde a = L - ap/2 (ponto médio de contato ao longo do comprimento L)
        // e L = comprimento em balanço da ferramenta.
        // NOTA: para usar o modelo completo com Z-Buffer, chame CalculateCuttingAnalysis.
        public double[] CalculateDeflection(double[][] pathPoints, double[] forces, double L, double E, double I)
        {
            int numPoints = forces.Length;
            double[] deflections = new double[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                // Estima ap a partir da variação de Z entre pontos consecutivos
                double ap = 0;
                if (i > 0)
                    ap = Math.Abs(pathPoints[i][2] - pathPoints[i - 1][2]);
                if (ap < 0.1) ap = 0.1; // valor mínimo para evitar divisão por zero

                // Ponto de aplicação da força resultante (centro do engajamento axial)
                double a = Math.Max(0.1, L - ap / 2.0);

                // Fórmula correta para viga engastada com carga pontual em 'a':
                // δ = F * a² * (3L - a) / (6 * E * I)
                deflections[i] = (forces[i] * Math.Pow(a, 2) * (3.0 * L - a)) / (6.0 * E * I);
            }
            return deflections;
        }
    }

    // CLASSE INTERNA: Simulador volumétrico Z-Buffer (Preservada integralmente)
    public class VolumetricEngine
    {
        private double[,] zBuffer;
        private double resolucaoMalha = 0.5;
        private int tamanhoX, tamanhoY;
        private double minX_Global, minY_Global;

        public void InicializarBlankPorTrajetoria(double[][] points, double diametro, double apMaximo)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (var p in points)
            {
                if (p[0] < minX) minX = p[0];
                if (p[1] < minY) minY = p[1];
                if (p[0] > maxX) maxX = p[0];
                if (p[1] > maxY) maxY = p[1];
                if (p[2] > maxZ) maxZ = p[2];
            }

            minX -= diametro; minY -= diametro;
            maxX += diametro; maxY += diametro;

            minX_Global = minX;
            minY_Global = minY;

            tamanhoX = (int)((maxX - minX) / resolucaoMalha) + 1;
            tamanhoY = (int)((maxY - minY) / resolucaoMalha) + 1;

            zBuffer = new double[tamanhoX, tamanhoY];
            double alturaMaterial = maxZ + apMaximo;

            for (int i = 0; i < tamanhoX; i++)
            {
                for (int j = 0; j < tamanhoY; j++)
                {
                    zBuffer[i, j] = alturaMaterial;
                }
            }
        }

        public void SimularCorte(double[] pontoAtual, double raioFerramenta, out double aeReal, out double apReal)
        {
            int pontosCortados = 0;
            double volumeRemovido = 0;

            int centroI = (int)((pontoAtual[0] - minX_Global) / resolucaoMalha);
            int centroJ = (int)((pontoAtual[1] - minY_Global) / resolucaoMalha);
            int raioEmIndices = (int)(raioFerramenta / resolucaoMalha);

            int minI = Math.Max(0, centroI - raioEmIndices);
            int maxI = Math.Min(tamanhoX - 1, centroI + raioEmIndices);
            int minJ = Math.Max(0, centroJ - raioEmIndices);
            int maxJ = Math.Min(tamanhoY - 1, centroJ + raioEmIndices);

            double zFerramenta = pontoAtual[2];

            for (int i = minI; i <= maxI; i++)
            {
                for (int j = minJ; j <= maxJ; j++)
                {
                    double distQuadrado = Math.Pow(i - centroI, 2) + Math.Pow(j - centroJ, 2);

                    if (distQuadrado <= Math.Pow(raioEmIndices, 2))
                    {
                        if (zFerramenta < zBuffer[i, j])
                        {
                            volumeRemovido += (zBuffer[i, j] - zFerramenta) * Math.Pow(resolucaoMalha, 2);
                            zBuffer[i, j] = zFerramenta;
                            pontosCortados++;
                        }
                    }
                }
            }

            double areaFerramenta = Math.PI * Math.Pow(raioFerramenta, 2);
            double areaCortada = pontosCortados * Math.Pow(resolucaoMalha, 2);

            double taxaDeContatoRadial = areaCortada / areaFerramenta;
            aeReal = Math.Min(raioFerramenta * 2.0, taxaDeContatoRadial * (raioFerramenta * 2.0));
            apReal = (areaCortada > 0) ? volumeRemovido / areaCortada : 0;
        }
    }
}