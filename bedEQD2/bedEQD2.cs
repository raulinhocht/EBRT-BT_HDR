using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
// [assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
  public class Script
  {
    public Script()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
    {
        //CODE 11 - BED Y EQD2 -------------------------------------------------------------------------------------------20241217

        double alphaBeta_10 = 10;
        double alphaBeta_3 = 3;

        //EQD2
        double EQD2_CTOT = 0;
        double EQD2_V_HDRTOT = 0;
        double EQD2_R_HDRTOT = 0;
        double EQD2_S_HDRTOT = 0;

        double EQD2_P = 0;
        double EQD2_V = 0;
        double EQD2_R = 0;
        double EQD2_S = 0;

        foreach (Course curso in context.Patient.Courses)
        {
            if (curso.Id == "1. Cervix")
            {
                foreach (var planext in curso.ExternalPlanSetups)
                {
                    Structure PTV = planext.StructureSet.Structures.FirstOrDefault(xP => xP.Id == "PTV_56");
                    Structure Recto = context.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto");
                    Structure Vejiga = context.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga");
                    Structure Sigma = context.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma");

                    // Obtener los datos del DVH
                    DVHData dvhDataP = planext.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = planext.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = planext.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = planext.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                    if (dvhDataP != null)
                    {
                        // Buscar punto en el DVH
                        double dosisP = 5390; // Dosis PTV Ref 95.85; 5500 cGy
                        double dosisR = 4940; // Dosis RECTO Ref 45.36; 1500 cGy
                        double dosisV = 5540; // Dosis VEJIGA Ref 282.54; 1100 cGy
                        double dosisS = 5040; // Dosis SIGMA Ref 35.5; 2200 cGy
                        DVHPoint? P90E0 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);//****************************************
                        DVHPoint P90 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);
                        DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                        DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                        DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                        //BED

                        double fracciones_EBRT = (double)planext.NumberOfFractions;
                        //double BED_P = dosisTotal * ( 1+ ((dosisTotal/fracciones_EBRT)/alphaBeta_10));
                        double BED_P = dosisP * 0.01 * (1 + ((dosisP * 0.01 / fracciones_EBRT) / alphaBeta_10));
                        double BED_R = dosisR * 0.01 * (1 + ((dosisR * 0.01 / fracciones_EBRT) / alphaBeta_3));
                        double BED_V = dosisV * 0.01 * (1 + ((dosisV * 0.01 / fracciones_EBRT) / alphaBeta_3));
                        double BED_S = dosisS * 0.01 * (1 + ((dosisS * 0.01 / fracciones_EBRT) / alphaBeta_3));

                        //EQD2
                        EQD2_P = BED_P / (1 + (2 / alphaBeta_10));
                        EQD2_V = BED_V / (1 + (2 / alphaBeta_3));
                        EQD2_R = BED_R / (1 + (2 / alphaBeta_3));
                        EQD2_S = BED_S / (1 + (2 / alphaBeta_3));

                        if (P90E0 != null)
                        {
                            // Usamos StringBuilder para estructurar la salida
                            var sb = new System.Text.StringBuilder();

                            // Encabezado
                            sb.AppendLine($"Course: {curso.Id}"); //nombre del curso
                            sb.AppendLine($"Plan EBRT: {planext.Id}"); //nombre del plan
                            sb.AppendLine($"Dosis prescrita: {planext.TotalDose}"); //nombre del plan
                            sb.AppendLine($"Número de fracciones: {planext.NumberOfFractions}"); //nombre del plan

                            sb.AppendLine("------------------------------------------------------------------");
                            sb.AppendLine("|       Estructura     | Dosis (cGy) | Volumen (cm³) |   BED (Gy)   |   EQD2 (Gy)   |");
                            sb.AppendLine("------------------------------------------------------------------");

                            // Filas con datos
                            sb.AppendLine($"| PTV                    |     {dosisP,12}|{P90.Volume,14:F2}| {BED_P,15:F2} | {EQD2_P,15:F2} |");
                            sb.AppendLine($"| RECTO               |     {dosisR,12} | {R90.Volume,14:F2} | {BED_R,15:F2} | {EQD2_R,15:F2} |");
                            sb.AppendLine($"| VEJIGA               |     {dosisV,12} | {V90.Volume,14:F2} | {BED_V,15:F2} | {EQD2_V,15:F2} |");
                            sb.AppendLine($"| SIGMA               |     {dosisS,12} | {S90.Volume,14:F2} | {BED_S,15:F2} | {EQD2_S,15:F2} |");

                            // Pie de tabla
                            sb.AppendLine("------------------------------------------------------------------");

                            // Mostrar resultado en MessageBox
                            MessageBox.Show(sb.ToString(), "Resumen de Datos");
                        }
                    }

                    else
                    {
                        MessageBox.Show("No hay datos disponibles");
                    }
                }
            }

            else if (curso.Id == "2. Fletcher")
            {
                foreach (var planbt in curso.BrachyPlanSetups) // Accede a planes BT|
                {
                    // Obtener la estructura HR-CTV
                    Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(xC => xC.Id == "HR-CTV");
                    Structure Recto = planbt.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto-HDR");
                    Structure Vejiga = planbt.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga-HDR");
                    Structure Sigma = planbt.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma-HDR");

                    // Obtener los datos del DVH
                    DVHData dvhDataC = planbt.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = planbt.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = planbt.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = planbt.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                    if (dvhDataC != null)
                    {
                        // Buscar punto en el DVH
                        double dosisC = 430; // Dosis CTV Ref 46; 510 cGy
                        double dosisR = 387; // Dosis RECTO Ref 60.9; 50
                        double dosisV = 370; // Dosis VEJIGA Ref 237.2; 80
                        double dosisS = 70; // Dosis SIGMA Ref 9.86; 40
                        DVHPoint? C90BT0 = dvhDataC.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisC) < 0.01);//****************************************
                        DVHPoint C90 = dvhDataC.CurveData.FirstOrDefault(ptoC => Math.Abs(ptoC.DoseValue.Dose - dosisC) < 0.01);
                        DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                        DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                        DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                        //BED
                        //0.01 cGy to Gy
                        double fracciones_BT = (double)planbt.NumberOfFractions;
                        double BED_C = dosisC * 0.01 * (1 + ((dosisC * 0.01 / fracciones_BT) / alphaBeta_10));
                        double BED_R_HDR = dosisR * 0.01 * (1 + ((dosisR * 0.01 / fracciones_BT) / alphaBeta_3));
                        double BED_V_HDR = dosisV * 0.01 * (1 + ((dosisV * 0.01 / fracciones_BT) / alphaBeta_3));
                        double BED_S_HDR = dosisS * 0.01 * (1 + ((dosisS * 0.01 / fracciones_BT) / alphaBeta_3));

                        //EQD2
                        double EQD2_C = BED_C / (1 + (2 / alphaBeta_10));
                        double EQD2_V_HDR = BED_V_HDR / (1 + (2 / alphaBeta_3));
                        double EQD2_R_HDR = BED_R_HDR / (1 + (2 / alphaBeta_3));
                        double EQD2_S_HDR = BED_S_HDR / (1 + (2 / alphaBeta_3));

                        EQD2_CTOT = EQD2_C + EQD2_CTOT;
                        EQD2_V_HDRTOT = BED_V_HDR + EQD2_V_HDRTOT;
                        EQD2_R_HDRTOT = BED_R_HDR + EQD2_R_HDRTOT;
                        EQD2_S_HDRTOT = BED_S_HDR + EQD2_S_HDRTOT;

                        if (C90BT0 != null)
                        {
                            // Usamos StringBuilder para estructurar la salida
                            var sb = new System.Text.StringBuilder();

                            // Encabezado
                            sb.AppendLine($"Course: {curso.Id}"); //nombre del curso
                            sb.AppendLine($"Plan EBRT: {planbt.Id}"); //nombre del plan
                            sb.AppendLine($"Dosis prescrita: {planbt.TotalDose}"); //nombre del plan
                            sb.AppendLine($"Número de fracciones: {planbt.NumberOfFractions}"); //nombre del plan

                            sb.AppendLine("------------------------------------------------------------------");
                            sb.AppendLine("|       Estructura     | Dosis (cGy) | Volumen (cm³) |   BED (Gy)   |   EQD2 (Gy)   |");
                            sb.AppendLine("------------------------------------------------------------------");

                            // Filas con datos
                            sb.AppendLine($"| HR-CTV                    |     {dosisC,12}|{C90.Volume,14:F2}| {BED_C,15:F2} | {EQD2_C,15:F2} |");
                            sb.AppendLine($"| Recto-HDR               |     {dosisR,12} | {R90.Volume,14:F2} | {BED_R_HDR,15:F2} | {EQD2_R_HDR,15:F2} |");
                            sb.AppendLine($"| Vejiga-HDR               |     {dosisV,12} | {V90.Volume,14:F2} | {BED_V_HDR,15:F2} | {EQD2_V_HDR,15:F2} |");
                            sb.AppendLine($"| Sigma-HDR               |     {dosisS,12} | {S90.Volume,14:F2} | {BED_S_HDR,15:F2} | {EQD2_S_HDR,15:F2} |");

                            // Pie de tabla
                            sb.AppendLine("------------------------------------------------------------------");

                            // Mostrar resultado en MessageBox
                            MessageBox.Show(sb.ToString(), "Resumen de Datos");
                        }
                    }
                    else
                    {
                        MessageBox.Show("No hay datos disponibles");
                    }
                }
            }
            double CTVT = EQD2_P + EQD2_CTOT;
            double RECTOT = EQD2_R + EQD2_R_HDRTOT;
            double VEJIGAT = EQD2_V + EQD2_V_HDRTOT;
            double SIGMAT = EQD2_S + EQD2_S_HDRTOT;

            MessageBox.Show(
                $"DOSIS TOTAL EBRT + BT-HDR\n\n" +
                $"HR-CTV----- Dosis: {CTVT:F2} Gy\n" +
                $"RECTO----- Dosis: {RECTOT:F2} Gy\n" +
                $"VEJIGA----- Dosis: {VEJIGAT:F2} Gy\n" +
                $"SIGMA----- Dosis: {SIGMAT:F2} Gy\n");
        }

        }
    }
}
