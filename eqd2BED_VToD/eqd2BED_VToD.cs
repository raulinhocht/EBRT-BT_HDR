using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
//using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.IO.Packaging;

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
            //CODE 12 - Volumen a dosis -------------------------------------------------------------------------------------------20250106
            foreach (Course curso in context.Patient.Courses)
            {
                //Factores alpha y beta
                double alphaBeta_10 = 10, alphaBeta_3 = 3;

                double EQD2_P100 = 0, EQD2_P90 = 0, EQD2_V = 0, EQD2_R = 0, EQD2_S = 0;
                double EQD2_C100 = 0, EQD2_C90 = 0, EQD2_V_HDR = 0, EQD2_R_HDR = 0, EQD2_S_HDR = 0;
                double EQD2_C100_T_BT = 0, EQD2_C90_T_BT = 0, EQD2_V_HDR_T_BT = 0, EQD2_R_HDR_T_BT = 0, EQD2_S_HDR_T_BT = 0;

                double EQD2_C100_TOTAL = 0, EQD2_C90_TOTAL = 0, EQD2_V_HDR_TOTAL = 0, EQD2_R_HDR_TOTAL = 0, EQD2_S_HDR_TOTAL = 0;

                //Volumen a evaluar
                double targetVolumeRel90 = 90; // Volumen objetivo en %       ------- PTV
                double targetVolumeRel100 = 100; // Volumen objetivo en %
                double targetVolumeAbs2 = 2; // Volumen objetivo en cm³       ------- OAR
                
                //var DvhObjective = 0;
                //DvhObjective = D2cc[cGy];

                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        // Parámetros de entrada
                        string structureIdP = "PTV_56"; // Nombre de la estructura en el plan
                        string structureIdR = "Recto"; // Nombre de la estructura en el plan
                        string structureIdV = "Vejiga"; // Nombre de la estructura en el plan
                        string structureIdS = "Sigma"; // Nombre de la estructura en el plan

                        //context.ExternalPlanSetup.StructureSet.Structures.FirstOrDefault();

                        //Estructuras EBRT
                        Structure PTV = planext.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdP);
                        Structure Recto = planext.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdR);
                        Structure Vejiga = planext.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdV);
                        Structure Sigma = planext.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdS);

                        // Obtener los datos DVH de la estructura
                        DVHData dvhDataP = planext.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.Relative, 1 /*Resolución DVH*/);
                        DVHData dvhDataR = planext.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        DVHData dvhDataV = planext.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        DVHData dvhDataS = planext.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        DVHPoint? dvhPointP100 = dvhDataP.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeRel100) < 0.01);
                        DVHPoint? dvhPointP90 = dvhDataP.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeRel90) < 0.01);
                        DVHPoint? dvhPointR = dvhDataR.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);
                        DVHPoint? dvhPointV = dvhDataV.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);
                        DVHPoint? dvhPointS = dvhDataS.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);

                        if (dvhPointP100.HasValue)
                        {
                            DoseValue doseAtVolumeP100 = dvhPointP100.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeP90 = dvhPointP90.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeR = dvhPointR.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeV = dvhPointV.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeS = dvhPointS.Value.DoseValue; // Obtener la dosis en el punto encontrado

                            double dosisP100 = doseAtVolumeP100.Dose * 0.01;
                            double dosisP90 = doseAtVolumeP90.Dose * 0.01;
                            double dosisR = doseAtVolumeR.Dose * 0.01;
                            double dosisV = doseAtVolumeV.Dose * 0.01;
                            double dosisS = doseAtVolumeS.Dose * 0.01;

                            //BED
                            double fracciones_EBRT = (double)planext.NumberOfFractions;
                            //double BED_P = dosisTotal * ( 1+ ((dosisTotal/fracciones_EBRT)/alphaBeta_10));
                            double BED_P100 = dosisP100 * (1 + ((dosisP100 / fracciones_EBRT) / alphaBeta_10));
                            double BED_P90 = dosisP90 * (1 + ((dosisP90 / fracciones_EBRT) / alphaBeta_10));
                            double BED_R = dosisR * (1 + ((dosisR / fracciones_EBRT) / alphaBeta_3));
                            double BED_V = dosisV * (1 + ((dosisV / fracciones_EBRT) / alphaBeta_3));
                            double BED_S = dosisS * (1 + ((dosisS / fracciones_EBRT) / alphaBeta_3));

                            //EQD2
                            EQD2_P100 = BED_P100 / (1 + (2 / alphaBeta_10));
                            EQD2_P90 = BED_P90 / (1 + (2 / alphaBeta_10));
                            EQD2_V = BED_V / (1 + (2 / alphaBeta_3));
                            EQD2_R = BED_R / (1 + (2 / alphaBeta_3));
                            EQD2_S = BED_S / (1 + (2 / alphaBeta_3));

                            var datos = new (string Estructura, double Dosis, double Volumen, double BED, double EQD2)[]
                            {
                                ("PTV100", dosisP100, targetVolumeRel100, BED_P100, EQD2_P100),
                                ("PTV90", dosisP90, targetVolumeRel90, BED_P90, EQD2_P90),
                                ("RECTO", dosisR, targetVolumeAbs2, BED_R, EQD2_R),
                                ("VEJIGA", dosisV, targetVolumeAbs2, BED_V, EQD2_V),
                                ("SIGMA", dosisS, targetVolumeAbs2, BED_S, EQD2_S)
                            };

                            // Usamos StringBuilder para estructurar la salida
                            var sb = new StringBuilder();

                            // Encabezado
                            sb.AppendLine($"Curso: {curso.Id}"); //nombre del curso
                            sb.AppendLine($"Plan EBRT: {planext.Id}"); //nombre del plan
                            sb.AppendLine($"Dosis prescrita: {planext.TotalDose}"); //nombre del plan
                            sb.AppendLine($"Número de fracciones: {planext.NumberOfFractions}"); //nombre del plan

                            sb.AppendLine("--------------------------------------------------------------------------------------");
                            sb.AppendLine("|       Estructura     | Dosis (Gy) |  Volumen (cm³)   |   BED (Gy)   |  EQD2 (Gy)  |");
                            sb.AppendLine("--------------------------------------------------------------------------------------");

                            foreach (var dato in datos)
                            {
                                sb.AppendLine($"| {dato.Estructura,-20} | {dato.Dosis,10} | {dato.Volumen,14:F2} | {dato.BED,10:F2} | {dato.EQD2,10:F2} |");
                            }

                            sb.AppendLine("--------------------------------------------------------------------------------------");

                            MessageBox.Show(sb.ToString(), "Resumen de Datos");

                        }
                        else
                        {
                            MessageBox.Show($"No se encontró una dosis que corresponda al volumen objetivo de {targetVolumeRel100} cm³.", "Resultado");
                        }
                    }
                }

                else if (curso.Id == "2. Fletcher")
                {
                    foreach (var planbt in curso.BrachyPlanSetups)
                    {
                        // Parámetros de entrada
                        string structureIdP = "HR-CTV"; // Nombre de la estructura en el plan
                        string structureIdR = "Recto-HDR"; // Nombre de la estructura en el plan
                        string structureIdV = "Vejiga-HDR"; // Nombre de la estructura en el plan
                        string structureIdS = "Sigma-HDR"; // Nombre de la estructura en el plan

                        //Estructuras EBRT
                        Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdP);
                        Structure Recto_HDR = planbt.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdR);
                        Structure Vejiga_HDR = planbt.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdV);
                        Structure Sigma_HDR = planbt.StructureSet.Structures.FirstOrDefault(s => s.Id == structureIdS);

                        // Obtener los datos DVH de la estructura
                        DVHData dvhDataP = planbt.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.Relative, 1 /*Resolución DVH*/);
                        DVHData dvhDataR = planbt.GetDVHCumulativeData(Recto_HDR, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        DVHData dvhDataV = planbt.GetDVHCumulativeData(Vejiga_HDR, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        DVHData dvhDataS = planbt.GetDVHCumulativeData(Sigma_HDR, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        DVHPoint? dvhPointC100 = dvhDataP.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeRel100) < 0.01);
                        DVHPoint? dvhPointC90 = dvhDataP.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeRel90) < 0.01);
                        DVHPoint? dvhPointR_HDR = dvhDataR.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);
                        DVHPoint? dvhPointV_HDR = dvhDataV.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);
                        DVHPoint? dvhPointS_HDR = dvhDataS.CurveData.FirstOrDefault(point => Math.Abs(point.Volume - targetVolumeAbs2) < 0.01);

                        if (dvhPointC100.HasValue)
                        {
                            DoseValue doseAtVolumeC100 = dvhPointC100.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeC90 = dvhPointC90.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeR_HDR = dvhPointR_HDR.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeV_HDR = dvhPointV_HDR.Value.DoseValue; // Obtener la dosis en el punto encontrado
                            DoseValue doseAtVolumeS_HDR = dvhPointS_HDR.Value.DoseValue; // Obtener la dosis en el punto encontrado

                            double dosisC100 = doseAtVolumeC100.Dose * 0.01;
                            double dosisC90 = doseAtVolumeC90.Dose * 0.01;
                            double dosisR_HDR = doseAtVolumeR_HDR.Dose * 0.01;
                            double dosisV_HDR = doseAtVolumeV_HDR.Dose * 0.01;
                            double dosisS_HDR = doseAtVolumeS_HDR.Dose * 0.01;

                            //BED
                            double fracciones_BT = (double)planbt.NumberOfFractions;
                            //double BED_P = dosisTotal * ( 1+ ((dosisTotal/fracciones_EBRT)/alphaBeta_10));
                            double BED_C100 = dosisC100 * (1 + ((dosisC100 / fracciones_BT) / alphaBeta_10));
                            double BED_C90 = dosisC90 * (1 + ((dosisC90 / fracciones_BT) / alphaBeta_10));
                            double BED_R_HDR = dosisR_HDR * (1 + ((dosisR_HDR / fracciones_BT) / alphaBeta_3));
                            double BED_V_HDR = dosisV_HDR * (1 + ((dosisV_HDR / fracciones_BT) / alphaBeta_3));
                            double BED_S_HDR = dosisS_HDR * (1 + ((dosisS_HDR / fracciones_BT) / alphaBeta_3));

                            //EQD2
                            EQD2_C100 = BED_C100 / (1 + (2 / alphaBeta_10));
                            EQD2_C90 = BED_C90 / (1 + (2 / alphaBeta_10));
                            EQD2_V_HDR = BED_V_HDR / (1 + (2 / alphaBeta_3));
                            EQD2_R_HDR = BED_R_HDR / (1 + (2 / alphaBeta_3));
                            EQD2_S_HDR = BED_S_HDR / (1 + (2 / alphaBeta_3));

                            //EQD2 TOTAL
                            EQD2_C100_T_BT = EQD2_C100 + EQD2_C100_T_BT;
                            EQD2_C90_T_BT = EQD2_C90 + EQD2_C90_T_BT;
                            EQD2_V_HDR_T_BT = EQD2_V_HDR + EQD2_V_HDR_T_BT;
                            EQD2_R_HDR_T_BT = EQD2_R_HDR + EQD2_R_HDR_T_BT;
                            EQD2_S_HDR_T_BT = EQD2_S_HDR + EQD2_S_HDR_T_BT;

                            var datos = new (string Estructura, double Dosis, double Volumen, double BED, double EQD2)[]
                            {
                                ("CTV100", dosisC100, targetVolumeRel100, BED_C100, EQD2_C100),
                                ("CTV90", dosisC90, targetVolumeRel90, BED_C90, EQD2_C90),
                                ("RECTO-HDR", dosisR_HDR, targetVolumeAbs2, BED_R_HDR, EQD2_R_HDR),
                                ("VEJIGA-HDR", dosisV_HDR, targetVolumeAbs2, BED_V_HDR, EQD2_V_HDR),
                                ("SIGMA-HDR", dosisS_HDR, targetVolumeAbs2, BED_S_HDR, EQD2_S_HDR)
                            };

                            // Usamos StringBuilder para estructurar la salida
                            var sb = new StringBuilder();

                            // Encabezado
                            sb.AppendLine($"Curso: {curso.Id}"); //nombre del curso
                            sb.AppendLine($"Plan BT: {planbt.Id}"); //nombre del plan
                            sb.AppendLine($"Dosis prescrita: {planbt.TotalDose}"); //nombre del plan
                            sb.AppendLine($"Número de fracciones: {planbt.NumberOfFractions}"); //nombre del plan

                            sb.AppendLine("--------------------------------------------------------------------------------------");
                            sb.AppendLine("|       Estructura     | Dosis (Gy) |  Volumen (cm³)   |   BED (Gy)   |  EQD2 (Gy)  |");
                            sb.AppendLine("--------------------------------------------------------------------------------------");

                            foreach (var dato in datos)
                            {
                                sb.AppendLine($"| {dato.Estructura,-20} | {dato.Dosis,10} | {dato.Volumen,14:F2} | {dato.BED,10:F2} | {dato.EQD2,10:F2} |");
                            }

                            sb.AppendLine("--------------------------------------------------------------------------------------");

                            MessageBox.Show(sb.ToString(), "Resumen de Datos");

                        }
                        else
                        {
                            MessageBox.Show($"No se encontró datos disponibles");
                        }
                    }
                }

                EQD2_C100_TOTAL = EQD2_P100 + EQD2_C100_T_BT;
                EQD2_C90_TOTAL = EQD2_P90 + EQD2_C90_T_BT;
                EQD2_V_HDR_TOTAL = EQD2_V + EQD2_V_HDR_T_BT;
                EQD2_R_HDR_TOTAL = EQD2_R + EQD2_R_HDR_T_BT;
                EQD2_S_HDR_TOTAL = EQD2_S + EQD2_S_HDR_T_BT;

                MessageBox.Show(

                    $"DOSIS TOTAL EBRT + BT-HDR\n\n" +
                    $"HR-CTV100----- Dosis: {EQD2_C100_TOTAL:F2} Gy\n" +
                    $"HR-CTV90----- Dosis: {EQD2_C90_TOTAL:F2} Gy\n" +
                    $"RECTO----- Dosis: {EQD2_V_HDR_TOTAL:F2} Gy\n" +
                    $"VEJIGA----- Dosis: {EQD2_R_HDR_TOTAL:F2} Gy\n" +
                    $"SIGMA----- Dosis: {EQD2_S_HDR_TOTAL:F2} Gy\n"
                );
            }
        }
    }
}