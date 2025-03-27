using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.0.9")]
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("1.0")]

namespace VMS.TPS
{
    public class Script
    {
        public Script() { }
        //----------------------------------------------------------------------------------------------------------------------
        // Método para evaluar la aprobación global del plan
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsTreatmentPlanApproved(Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            foreach (var item in eqd2Total)
            {
                string structureId = item.Key;
                double eqd2Val = item.Value;
                if (constraints.TryGetValue(structureId, out var constraint))
                {
                    string evaluacion = EvaluateConstraints(eqd2Val, constraint);
                    // Si la evaluación contiene "No Aprobado", se considera que el plan no cumple
                    if (evaluacion.StartsWith("No Aprobado"))
                        return false;
                }
                else
                {
                    return false;// Si no se tiene definición para alguna estructura, asumimos que no cumple
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            //----------------------------------------------------------------------------------------------------------------------
            // Datos del paciente
            //----------------------------------------------------------------------------------------------------------------------
            string patientName = context.Patient.Name;
            string patientId1 = context.Patient.Id;
            string patientId2 = context.Patient.Hospital.Id;

            // Inicializamos valores de dosis y fracciones para EBRT y BT
            int totalFraccionesEBRT = 0, totalFraccionesBT = 0;
            double totalDosisEBRT = 0, totalDosisBT = 0;
            double alphaBetaTumor = 10, alphaBetaOAR = 3;
            string EBRT = "Cervix";
            string HDR_BT = "Braqui";

            // Procesamos los cursos para obtener fracciones y dosis
            foreach (Course curso in context.Patient.Courses)
            {
                if (curso.Id.Contains(EBRT) || curso.Id.Contains("EBRT") || curso.Id.Contains("CERVIX") || curso.Id.Contains("Cérvix"))
                {
                    foreach (var plan in curso.ExternalPlanSetups)
                    {
                        totalFraccionesEBRT = plan.NumberOfFractions ?? 0;
                        totalDosisEBRT = plan.TotalDose.Dose;
                    }
                }
                else if (curso.Id.Contains(HDR_BT) || curso.Id.Contains("Fletcher") || curso.Id.Contains("FLETCHER"))
                {
                    foreach (var plan in curso.BrachyPlanSetups)
                    {
                        totalFraccionesBT = plan.NumberOfFractions ?? 0;
                        totalDosisBT = plan.TotalDose.Dose;
                    }
                }
            }
            //----------------------------------------------------------------------------------------------------------------------
            // Encabezado del reporte
            //----------------------------------------------------------------------------------------------------------------------
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(" PRUEBA V30 Resumen Consolidado de Datos con EQD2, Ajuste por Tiempo y Evaluación");
            sb.AppendLine("=====================================================================================");
            sb.AppendLine($" Paciente: {patientName}");
            sb.AppendLine($" ID1: {patientId1}   |   ID2: {patientId2}");
            sb.AppendLine($" α/β Tumor: {alphaBetaTumor}   |   α/β OAR: {alphaBetaOAR}");
            sb.AppendLine("-------------------------------------------------------------------------------------");
            sb.AppendLine($"EBRT: Dosis Prescrita: {totalDosisEBRT} en {totalFraccionesEBRT} fracciones");
            sb.AppendLine($"BT: Dosis Prescrita: {totalDosisBT * 5} en {totalFraccionesBT * 5} fracciones");
            sb.AppendLine("-------------------------------------------------------------------------------------");
            //sb.AppendLine("=====================================================================================\n");

            // Diccionario para acumular EQD2 total de EBRT + HDR-BT
            Dictionary<string, double> eqd2Total = new Dictionary<string, double>
            {
                {"PTV+CTV", 0}, {"Recto", 0}, {"Vejiga", 0}, {"Sigma", 0}
            };

            // Parámetros de restricciones clínicas (Aim y Limit)
            var constraints = new Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)>
            {
                { "Recto",    (65.0, 75.0, "lessThan", "< 65 Gy", "< 75 Gy") },
                { "Vejiga",   (80.0, 90.0, "lessThan", "< 80 Gy", "< 90 Gy") },
                { "Sigma",    (70.0, 75.0, "lessThan", "< 70 Gy", "< 75 Gy") },
                { "PTV+CTV",  (85.0, 95.0, "range",    "> 85 Gy", "< 95 Gy") }
            };
            //----------------------------------------------------------------------------------------------------------------------
            // Procesamiento de cursos
            //----------------------------------------------------------------------------------------------------------------------
            foreach (Course curso in context.Patient.Courses)
            {
                double targetVolumeRel90 = 90;
                double targetVolumeAbs2 = 2;

                if (curso.Id.Contains(EBRT) || curso.Id.Contains("EBRT") || curso.Id.Contains("CERVIX") || curso.Id.Contains("Cérvix"))
                {
                    sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");


                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        if (planext.ApprovalStatus == PlanSetupApprovalStatus.Completed || planext.ApprovalStatus == PlanSetupApprovalStatus.CompletedEarly || planext.ApprovalStatus == PlanSetupApprovalStatus.Retired || planext.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved)
                        {
                            if (planext.NumberOfFractions == 28) //planext.ApprovalStatus == PlanSetupApprovalStatus.Completed || planext.ApprovalStatus == PlanSetupApprovalStatus.CompletedEarly || planext.ApprovalStatus == PlanSetupApprovalStatus.Retired || planext.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved)
                            {
                                ProcessEBRTPlan(planext, sb, alphaBetaTumor, alphaBetaOAR,
                                            targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                            }
                        }
                    }
                }
                else if (curso.Id.Contains(HDR_BT) || curso.Id.Contains("Fletcher") || curso.Id.Contains("FLETCHER"))
                {
                    sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
                    ProcessBrachytherapyPlans(curso, sb, alphaBetaTumor, alphaBetaOAR,
                                              targetVolumeRel90, targetVolumeAbs2, eqd2Total);
                }
            }
            //----------------------------------------------------------------------------------------------------------------------
            // Sección Total con comparación de Aims y Límites
            //----------------------------------------------------------------------------------------------------------------------
            sb.AppendLine("\n====================== SECCIÓN TOTAL EBRT + HDR-BT ======================");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  | Aim         | Limit        | Concepto Final                     |");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            foreach (var item in eqd2Total)
            {
                string structureId = item.Key;
                double eqd2Val = item.Value;

                if (constraints.TryGetValue(structureId, out var constraint))
                {
                    string concepto = EvaluateConstraints(eqd2Val, constraint);
                    sb.AppendLine(
                        $"| {structureId,-15} | {eqd2Val,-16:F2} | {constraint.aimText,-11} | {constraint.limitText,-11} | {concepto,-32} |"
                    );
                }
                else
                {
                    sb.AppendLine(
                        $"| {structureId,-15} | {eqd2Val,-16:F2} |      -      |      -      |       Sin definición        |"
                    );
                }
            }
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            //----------------------------------------------------------------------------------------------------------------------
            // Implementación de la elección de tratamiento
            //----------------------------------------------------------------------------------------------------------------------
            if (IsTreatmentPlanApproved(eqd2Total, constraints))
            {
                sb.AppendLine("\nEl plan de tratamiento cumple con los criterios y ESTÁ APROBADO.");
                // Aquí se podría activar la opción de seleccionar este plan para tratamiento
            }
            else
            {
                sb.AppendLine("\nEl plan de tratamiento NO cumple con los criterios y NO está aprobado.");
                // Aquí se podrían activar otras opciones o replanificar
            }

            //----------------------------------------------------------------------------------------------------------------------
            // Mostrar el reporte en una ventana
            //----------------------------------------------------------------------------------------------------------------------
            Window messageWindow = new Window
            {
                Title = "Resumen de Datos con EQD2, Ajuste por Tiempo y Selección de Tratamiento",
                Content = new TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 1000,
                Height = 700
            };

            messageWindow.ShowDialog();
        }
        //----------------------------------------------------------------------------------------------------------------------
        //--------------------------------------------------FUNCIONES-----------------------------------------------------------
        // Función para evaluar si un valor EQD2 cumple con los criterios clínicos
        //----------------------------------------------------------------------------------------------------------------------
        // Parámetros para el ajuste por tiempo:-------------------------------------------------------------ttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttt
        double totalTime = 28.0;  // Valor fijo de ejemplo (se puede modificar)
        double Tdelay = 28.0; //RayosContraCancer2022
        double k = 0.6; //EMBRACE-II, 2021 \cite{tanderup2021embrace}
        private string EvaluateConstraints(double eqd2Val, (double aimValue, double limitValue, string type, string aimText, string limitText) constraint)
        {
            double aimVal = constraint.aimValue;
            double limitVal = constraint.limitValue;
            string tipo = constraint.type;

            if (tipo == "lessThan")
            {
                if (eqd2Val <= aimVal)
                    return "Aprobado (Aim cumplido)";
                else if (eqd2Val <= limitVal)
                    return "Aprobado (Dentro de Límite)";
                else
                    return "No Aprobado (Supera Límite)";
            }
            else if (tipo == "range")
            {
                if (eqd2Val >= aimVal && eqd2Val <= limitVal)
                    return "Aprobado (En rango)";
                else
                    return "No Aprobado (Fuera de rango)";
            }
            return "Sin definición";
        }
        //----------------------------------------------------------------------------------------------------------------------
        // ------------------- Procesamiento de EBRT  -------------------
        //----------------------------------------------------------------------------------------------------------------------
        private void ProcessEBRTPlan(
            ExternalPlanSetup plan,
            StringBuilder sb,
            double alphaBetaTumor,
            double alphaBetaOAR,
            double targetVolumeRel90,
            double targetVolumeAbs2,
            Dictionary<string, double> eqd2Total)
        {
            //sb.AppendLine($"Dosis Prescrita: {plan.TotalDose} en {plan.NumberOfFractions} fracciones");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura  | Dosis at Volume [Gy] | EQD2 [Gy]   |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("PTV_56", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR, "Sigma")
            };

            // Variable para almacenar el EQD2 total de EBRT para PTV+CTV
            double totalDosisEBRT = 0;

            foreach (var (structureId, targetVolume, alphaBeta, key) in structures)
            {
                double eqd2 = ProcessEQD2ForPlan(plan, structureId, targetVolume, alphaBeta);
                double doseAtVolume = 0;
                if (targetVolume == targetVolumeAbs2)
                {
                    doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, targetVolume) / 100.0;
                }
                else
                {
                    doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume) / 100.0;
                }
                //double doseAtVolume = GetDoseAtVolume(plan, structureId, targetVolume) / 100.0;
                sb.AppendLine($"| {structureId,-12} | {doseAtVolume,-20:F2} | {eqd2,-10:F2} |");
                eqd2Total[key] += eqd2;

                // Almacenamos el valor para PTV+CTV
                if (key == "PTV+CTV")
                {
                    totalDosisEBRT = eqd2;
                }
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }
        //----------------------------------------------------------------------------------------------------------------------
        // ------------------- Procesamiento de BT ------------------
        //----------------------------------------------------------------------------------------------------------------------
        private void ProcessBrachytherapyPlans(
            Course course,
            StringBuilder sb,
            double alphaBetaTumor,
            double alphaBetaOAR,
            double targetVolumeRel90,
            double targetVolumeAbs2,
            Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups.OrderBy(p => p.Id).ToList();
            if (!plans.Any()) return;

            sb.AppendLine("\n Plan de Braquiterapia - Dosis por Sesión con EQD2 ");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            sb.Append("|Estruc/Dosis[Gy]");
            for (int i = 1; i <= plans.Count; i++)
            {
                if (i == 1)
                    sb.Append("| Fx #1  | EQD2 1 ");
                else
                    sb.Append($"| Fx #{i} | EQD2 {i} ");
            }
            sb.AppendLine("|   totalBED  |  totalEQD2  |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");

            var structures = new[]
            {
                ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"),
                ("Recto-HDR", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga-HDR", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma-HDR", targetVolumeAbs2, alphaBetaOAR, "Sigma")
            };

            foreach (var (structureId, defaultVolume, alphaBeta, key) in structures)
            {
                sb.Append($"| {structureId,-15} ");
                double totalBED = 0, totalEQD2 = 0;

                foreach (var plan in plans)
                {
                    double volumeToUse = defaultVolume;
                    if (structureId == "HR-CTV")
                    {
                        var estructura = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
                        if (estructura != null)
                            volumeToUse = estructura.Volume * 0.9;// 90% del volumen
                    }

                    double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, volumeToUse);
                    double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
                    double bed = CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                    double eqd2 = CalculateEQD2(bed, alphaBeta);

                    totalBED += bed;
                    totalEQD2 += eqd2;
                    sb.Append($"| {doseAtVolume / 100,-12:F2} | {eqd2,-12:F2} ");
                }
                sb.Append($"| {totalBED,-10:F2} | {totalEQD2,-10:F2} |");
                sb.AppendLine();
                eqd2Total[key] += totalEQD2;
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
        }
        //----------------------------------------------------------------------------------------------------------------------
        // ------------------- Cálculos de BED y EQD2 -------------------
        //----------------------------------------------------------------------------------------------------------------------
        private double ProcessEQD2ForPlan(PlanSetup plan, string structureId, double targetVolume, double alphaBeta)
        {
            double doseAtVolume = GetDoseAtVolumeAbsoluta(plan, structureId, targetVolume);
            double dosePerFraction = doseAtVolume / (double)plan.NumberOfFractions;
            double bed = CalculateBED(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta);

            if (totalTime > Tdelay)
            {

                if (plan.PlanType == PlanType.ExternalBeam)
                {
                    return CalculateEQD2(bed, alphaBeta);
                }
                else
                {
                    return CalculateBEDWithTimeAdjustment(dosePerFraction, (double)plan.NumberOfFractions, alphaBeta, totalTime, Tdelay, k);
                }
            }
            else
            {
                return CalculateEQD2(bed, alphaBeta);
            }
        }

        private double CalculateBED(double dosePerFraction, double fractions, double alphaBeta)
        {
            return (dosePerFraction / 100.0) * fractions * (1 + ((dosePerFraction / 100.0) / alphaBeta));
        }
        private double CalculateBEDWithTimeAdjustment(double dosePerFraction, double fractions, double alphaBeta, double totalTime, double Tdelay, double k)
        {
            // Cálculo básico de BED sin ajuste de tiempo
            double bed_ = (dosePerFraction / 100.0) * fractions * (1 + (dosePerFraction / 100.0) / alphaBeta);

            // Si el tiempo total es mayor que el tiempo de retraso, ajustamos la BED
            bed_ -= k * (totalTime - Tdelay); // Ajuste de la repoblación
            return bed_;
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            return bed / (1 + (2.0 / alphaBeta));
        }
        
        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volume)
        {
            // Se utiliza volumen relativo para PTV y CTV
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            if (structure == null) return 0;
            return plan.GetDoseAtVolume(structure, volume, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
        }
        private double GetDoseAtVolumeAbsoluta(PlanSetup plan, string structureId, double volume)
        {
            // Se utiliza volumen absoluto para OARs
            var structure = plan.StructureSet.Structures.FirstOrDefault(s => s.Id == structureId);
            if (structure == null) return 0;
            return plan.GetDoseAtVolume(structure, volume, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute).Dose;
        }

    }
}