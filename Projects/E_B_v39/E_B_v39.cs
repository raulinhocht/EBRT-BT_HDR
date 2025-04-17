// código corre pero no imprime datos 
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
// using System.Runtime.CompilerServices; // No parece necesario aquí
using System.Collections.Generic;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media;
using System.Windows.Documents;
using System.IO;
using System.Windows.Shapes;
using System.Windows.Input; // Necesario para Cursors
using System.Printing; // Necesario para PrintDialog

[assembly: AssemblyVersion("1.0.0.9")] // Mantén tus versiones o actualízalas según necesites
[assembly: AssemblyFileVersion("1.0.0.9")]
[assembly: AssemblyInformationalVersion("1.0")] // Puedes cambiar esto a "V36" si lo deseas

namespace VMS.TPS
{
    public class Script
    {
        //----------------------------------------------------------------------------------------------------------------------
        // Constantes y parámetros clínicos (Sin cambios)
        //----------------------------------------------------------------------------------------------------------------------
        private const double alphaBetaTumor = 10.0;
        private const double alphaBetaOAR = 3.0;
        private const double targetVolumeRel90 = 90.0; // 90% para PTV/CTV
        private const double targetVolumeAbs2 = 2.0;   // 2cc para OARs
        private const double totalTime = 28.0;        // Tiempo total de tratamiento (días)
        private const double Tdelay = 28.0;          // Tiempo de retraso para repoblación
        private const double k = 0.6;                // Constante de repoblación

        //----------------------------------------------------------------------------------------------------------------------
        // Método principal de ejecución
        //----------------------------------------------------------------------------------------------------------------------
        public void Execute(ScriptContext context)
        {
            if (context?.Patient == null)
            {
                MessageBox.Show("No hay un paciente cargado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Inicialización de variables (Sin cambios)
            StringBuilder sb = new StringBuilder();
            string patientName = context.Patient.Name;
            string patientId = context.Patient.Id;
            double totalDosisEBRT_EQD2_PTV = 0; // Renombrado para claridad
            double totalDosisBT_Phys_HRCTV = 0; // Renombrado para claridad
            int totalFraccionesEBRT = 0;
            int totalFraccionesBT = 0;


            // Diccionario para acumular EQD2 total (Sin cambios)
            Dictionary<string, double> eqd2Total = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) // Usar comparador ignore-case
            {
                {"PTV+CTV", 0}, {"Recto", 0}, {"Vejiga", 0}, {"Sigma", 0}
            };

            // Restricciones clínicas (Sin cambios)
            // Usar claves que coincidan con las del diccionario eqd2Total (ignore-case ayuda)
            var constraints = new Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Recto",    (65.0, 75.0, "lessThan", "< 65 Gy", "< 75 Gy") },
                { "Vejiga",   (80.0, 90.0, "lessThan", "< 80 Gy", "< 90 Gy") },
                { "Sigma",    (70.0, 75.0, "lessThan", "< 70 Gy", "< 75 Gy") },
                { "PTV+CTV",  (85.0, 95.0, "range",    "> 85 Gy", "< 95 Gy") }
            };

            //----------------------------------------------------------------------------------------------------------------------
            // Encabezado del reporte (Sin cambios)
            //----------------------------------------------------------------------------------------------------------------------
            GenerateReportHeader(sb, patientName, patientId);

            //----------------------------------------------------------------------------------------------------------------------
            // Procesamiento de cursos (EBRT y BT)
            //----------------------------------------------------------------------------------------------------------------------
            Dictionary<string, List<double>> btDosesData = null;

            foreach (Course course in context.Patient.Courses.OrderBy(c => c.Id)) // Procesar cursos en orden
            {
                if (IsEBRTCourse(course.Id))
                {
                    ProcessEBRTCourse(course, sb, ref totalDosisEBRT_EQD2_PTV, ref totalFraccionesEBRT, eqd2Total);
                }
                else if (IsBrachyCourse(course.Id))
                {
                    // Pasar eqd2Total por referencia para que ProcessBrachyCourse pueda acumular directamente
                    btDosesData = ProcessBrachyCourse(course, sb, ref totalDosisBT_Phys_HRCTV, ref totalFraccionesBT, ref eqd2Total);
                }
            }

            //----------------------------------------------------------------------------------------------------------------------
            // Sección Total con comparación de Aims y Límites (Sin cambios)
            //----------------------------------------------------------------------------------------------------------------------
            GenerateTotalSection(sb, eqd2Total, constraints);

            //----------------------------------------------------------------------------------------------------------------------
            // Evaluación final del plan (Sin cambios)
            //----------------------------------------------------------------------------------------------------------------------
            EvaluateTreatmentPlan(sb, eqd2Total, constraints);

            //----------------------------------------------------------------------------------------------------------------------
            // Mostrar la ventana con los gráficos *** AJUSTE AQUÍ ***
            //----------------------------------------------------------------------------------------------------------------------
            // Pasar eqd2Total y constraints a la ventana para que la pestaña de resumen pueda usarlos
            ShowEnhancedReportWindow(sb, btDosesData, eqd2Total, constraints);
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares para procesamiento de cursos (Sin cambios)
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsEBRTCourse(string courseId)
        {
            // Usar comparación sin importar mayúsculas/minúsculas
            return courseId.IndexOf("Cervix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("EBRT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBrachyCourse(string courseId)
        {
            // Usar comparación sin importar mayúsculas/minúsculas
            return courseId.IndexOf("Braqui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("Fletcher", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   courseId.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de EBRT
        //----------------------------------------------------------------------------------------------------------------------
        // Cambiado ref double totalDosisEBRT a totalDosisEBRT_EQD2_PTV para claridad
        private void ProcessEBRTCourse(Course course, StringBuilder sb, ref double totalDosisEBRT_EQD2_PTV, ref int totalFraccionesEBRT, Dictionary<string, double> eqd2Total)
        {
            // Obtener esquema de tratamiento
            string treatmentScheme = GetTreatmentScheme(course);
            sb.AppendLine("\n========================== SECCIÓN EBRT ==========================");
            sb.AppendLine($"║ Curso: {course.Id} | Esquema: {treatmentScheme}   ║"); // Añadir ID del curso
            sb.AppendLine("------------------------------------------------------------------");

            // Buscar el primer plan aprobado o completado
            // Considerar filtrar por un ID específico si hay múltiples planes EBRT
            var approvedPlan = course.ExternalPlanSetups
                .Where(p => IsPlanApproved(p)) // Usa la función IsPlanApproved
                .OrderByDescending(p => p.CreationDateTime) // Tomar el más reciente aprobado/completado
                .FirstOrDefault();

            if (approvedPlan == null)
            {
                sb.AppendLine("⚠ No se encontró plan EBRT aprobado o completado en este curso.");
                return;
            }

            // Asegurarse que NumberOfFractions tiene valor
            if (!approvedPlan.NumberOfFractions.HasValue || approvedPlan.NumberOfFractions.Value <= 0)
            {
                sb.AppendLine($"⚠ El plan '{approvedPlan.Id}' no tiene un número de fracciones válido.");
                return;
            }

            totalFraccionesEBRT = approvedPlan.NumberOfFractions.Value;
            // No necesitamos la dosis física total aquí realmente, calcularemos EQD2 por estructura


            sb.AppendLine($"\nPlan seleccionado: {approvedPlan.Id} ({approvedPlan.ApprovalStatus})");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("| Estructura   | Dosis Física (Gy) | EQD2 (Gy)      |");
            sb.AppendLine("--------------------------------------------------");

            // Claves deben coincidir con eqd2Total y constraints (case-insensitive ayuda)
            var structures = new[] {
                // Nombre Estructura, Métrica Volumen, AlfaBeta, Clave Diccionario
                ("PTV_56", targetVolumeRel90, alphaBetaTumor, "PTV+CTV"), // Asumiendo que PTV_56 representa el PTV+CTV
                ("Recto", targetVolumeAbs2, alphaBetaOAR, "Recto"),
                ("Vejiga", targetVolumeAbs2, alphaBetaOAR, "Vejiga"),
                ("Sigma", targetVolumeAbs2, alphaBetaOAR, "Sigma")
                // Añade más estructuras si es necesario (e.g., Intestino, Fémures)
            };

            foreach (var (structureId, volumeParam, alphaBeta, key) in structures)
            {
                double doseAtVolumeRaw; // Dosis en cGy devuelta por la API

                if (key.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase)) // Usar Métrica Relativa para PTV/CTV
                {
                    doseAtVolumeRaw = GetDoseAtVolume(approvedPlan, structureId, volumeParam);
                }
                else // Usar Métrica Absoluta para OARs
                {
                    doseAtVolumeRaw = GetDoseAtVolumeAbsoluta(approvedPlan, structureId, volumeParam);
                }


                if (double.IsNaN(doseAtVolumeRaw))
                {
                    sb.AppendLine($"| {structureId,-12} | {"No Encontrada",-17} | {"N/A",-14} |");
                    continue; // Pasar a la siguiente estructura
                }
                if (doseAtVolumeRaw <= 0)
                {
                    sb.AppendLine($"| {structureId,-12} | {"0.00",-17} | {"0.00",-14} |");
                    // Añadir 0 al total si la dosis es 0
                    if (eqd2Total.ContainsKey(key))
                    {
                        eqd2Total[key] += 0.0;
                    }
                    continue;
                }


                double doseValueGy = doseAtVolumeRaw / 100.0; // Convertir cGy a Gy
                double dosePerFraction = doseValueGy / totalFraccionesEBRT;
                // Para EBRT, no aplicamos ajuste por tiempo aquí (se asume efecto completo)
                double bed = CalculateBED(dosePerFraction, totalFraccionesEBRT, alphaBeta);
                double eqd2 = CalculateEQD2(bed, alphaBeta);

                sb.AppendLine($"| {structureId,-12} | {doseValueGy,-17:F2} | {eqd2,-14:F2} |");

                // Acumular EQD2 en el diccionario global
                if (eqd2Total.ContainsKey(key))
                {
                    eqd2Total[key] += eqd2;
                }
                else
                {
                    // Opcional: Añadir la clave si no existe (aunque debería estar inicializada)
                    // eqd2Total.Add(key, eqd2);
                    sb.AppendLine($"* Advertencia: Clave '{key}' no inicializada en eqd2Total.");
                }


                // Guardar el EQD2 del PTV+CTV de este plan
                if (key.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase))
                {
                    totalDosisEBRT_EQD2_PTV = eqd2; // Guarda el EQD2 de este plan
                }
            }
            sb.AppendLine("--------------------------------------------------------------------------------------------------");
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Procesamiento de Braquiterapia
        //----------------------------------------------------------------------------------------------------------------------
        // Cambiado ref double totalDosisBT a totalDosisBT_Phys_HRCTV
        // Añadido ref Dictionary<string, double> eqd2Total para acumular directamente
        private Dictionary<string, List<double>> ProcessBrachyCourse(Course course, StringBuilder sb, ref double totalDosisBT_Phys_HRCTV, ref int totalFraccionesBT, ref Dictionary<string, double> eqd2Total)
        {
            var plans = course.BrachyPlanSetups
                             .Where(p => IsPlanApproved(p)) // Usar la función IsPlanApproved
                             .OrderBy(p => p.CreationDateTime) // Ordenar por fecha de creación
                             .ToList();

            if (!plans.Any()) return null; // No hay planes aprobados/completados en este curso

            totalFraccionesBT = plans.Count; // Número de fracciones = número de planes aprobados
            totalDosisBT_Phys_HRCTV = 0; // Reiniciar para este curso


            // Obtener esquema de tratamiento del primer plan como referencia
            string treatmentScheme = GetTreatmentScheme(course);

            sb.AppendLine("\n====================== SECCIÓN HDR-BT ======================");
            sb.AppendLine($"║ Curso: {course.Id} | Esquema: {treatmentScheme}   ║"); // Añadir ID del curso
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");
            // Encabezado ajustado dinámicamente al número de fracciones encontradas
            sb.Append("| Estructura       | Métrica       |");
            for (int i = 1; i <= plans.Count; i++) sb.Append($" Fx #{i,-7} |");
            // Columna extra para Total Físico y Total EQD2
            sb.AppendLine(" Total Phys | Total EQD2 |");
            sb.AppendLine("--------------------------------------------------------------------------------------------------------------");


            // Usar nombres exactos de la estructura en tus planes. Claves deben coincidir con eqd2Total y constraints.
            var structures = new[] {
                 ("HR-CTV", targetVolumeRel90, alphaBetaTumor, "PTV+CTV", "D90% [Gy]"), // HR-CTV contribuye a PTV+CTV total
                 ("Recto_HDR", targetVolumeAbs2, alphaBetaOAR, "Recto", "D2cc [Gy]"),     // Nombre específico BT
                 ("Vejiga_HDR", targetVolumeAbs2, alphaBetaOAR, "Vejiga", "D2cc [Gy]"),    // Nombre específico BT
                 ("Sigma_HDR", targetVolumeAbs2, alphaBetaOAR, "Sigma", "D2cc [Gy]")      // Nombre específico BT
             };

            Dictionary<string, List<double>> btDosesPerFractionPhys = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            // Dictionary<string, List<double>> btEQD2PerFraction = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase); // Si se necesita gráfico EQD2 por fracción

            foreach (var (structureId, volumeParam, alphaBeta, key, metric) in structures)
            {
                // Inicializar listas para esta estructura
                btDosesPerFractionPhys[structureId] = new List<double>();
                // btEQD2PerFraction[structureId] = new List<double>();


                sb.Append($"| {structureId,-15} | {metric,-12} |");
                double totalStructurePhysDose = 0;
                double totalStructureEQD2 = 0;

                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    double doseAtVolumeRaw = double.NaN; // Dosis en cGy devuelta por la API
                    double doseAtVolumeGy = 0;
                    double eqd2Fraction = 0;

                    // Lógica para obtener D90% relativo o D2cc absoluto
                    // Usar la clave 'key' para decidir la métrica es más robusto que el ID directo
                    if (key.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase)) // Métrica relativa para Target
                    {
                        doseAtVolumeRaw = GetDoseAtVolume(plan, structureId, volumeParam); // Usa D90 relativo
                    }
                    else // Métrica absoluta para OARs
                    {
                        doseAtVolumeRaw = GetDoseAtVolumeAbsoluta(plan, structureId, volumeParam); // Usa D2cc absoluto
                    }


                    if (!double.IsNaN(doseAtVolumeRaw) && doseAtVolumeRaw > 0)
                    {
                        doseAtVolumeGy = doseAtVolumeRaw / 100.0; // Convertir a Gy
                        totalStructurePhysDose += doseAtVolumeGy; // Acumular dosis física

                        // Asumir 1 fracción por plan de braqui para el cálculo de BED/EQD2
                        // Aplicar ajuste por tiempo para BT
                        double bed = CalculateBEDWithTimeAdjustment(doseAtVolumeGy, 1, alphaBeta, totalTime, Tdelay, k);
                        eqd2Fraction = CalculateEQD2(bed, alphaBeta);
                        totalStructureEQD2 += eqd2Fraction; // Acumular EQD2 de la estructura

                        // Acumular EQD2 al total global directamente usando la referencia
                        if (eqd2Total.ContainsKey(key))
                        {
                            eqd2Total[key] += eqd2Fraction;
                        }
                        else
                        {
                            sb.AppendLine($"* Advertencia: Clave '{key}' no inicializada en eqd2Total.");
                        }
                    }
                    else if (double.IsNaN(doseAtVolumeRaw))
                    {
                        doseAtVolumeGy = double.NaN; // Marcar como no encontrado
                        eqd2Fraction = double.NaN;
                        sb.Append($" {"N/E",-7} |"); // N/E = No Encontrado
                    }
                    else // Dosis es 0 o negativa
                    {
                        doseAtVolumeGy = 0;
                        eqd2Fraction = 0;
                        sb.Append($" {doseAtVolumeGy,-7:F2} |");
                        // Acumular 0 al total global
                        if (eqd2Total.ContainsKey(key))
                        {
                            eqd2Total[key] += 0.0;
                        }
                    }

                    // Añadir a la lista para gráficos (incluso si es NaN o 0)
                    btDosesPerFractionPhys[structureId].Add(doseAtVolumeGy);
                    // btEQD2PerFraction[structureId].Add(eqd2Fraction); // Si se necesita gráfico EQD2 por fracción


                    // Escribir valor solo si no es NaN
                    if (!double.IsNaN(doseAtVolumeGy))
                    {
                        sb.Append($" {doseAtVolumeGy,-7:F2} |");
                    }
                    // La celda para NaN ya se añadió arriba
                }

                // Escribir totales para la estructura
                sb.Append($" {totalStructurePhysDose,-10:F2} | {totalStructureEQD2,-10:F2} |");
                sb.AppendLine();
                sb.AppendLine("--------------------------------------------------------------------------------------------------------------");


                // Acumular dosis física total del HR-CTV para información
                if (key.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase)) // Si esta estructura es el target
                {
                    totalDosisBT_Phys_HRCTV = totalStructurePhysDose;
                }
            }

            // Devolver los datos de dosis físicas por fracción para los gráficos
            return btDosesPerFractionPhys;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Método para presentar esquema de tratamiento
        //----------------------------------------------------------------------------------------------------------------------
        private string GetTreatmentScheme(Course course)
        {
            // Para cursos de EBRT
            if (IsEBRTCourse(course.Id))
            {
                // Buscar el plan más relevante (ej. el más reciente aprobado/completado)
                var relevantPlan = course.ExternalPlanSetups
                                        .Where(p => IsPlanApproved(p))
                                        .OrderByDescending(p => p.CreationDateTime)
                                        .FirstOrDefault();

                if (relevantPlan != null && relevantPlan.NumberOfFractions.HasValue && relevantPlan.NumberOfFractions.Value > 0)
                {
                    int fractions = relevantPlan.NumberOfFractions.Value;
                    double dosePerFractionGy = 0;
                    double totalDoseGy = 0;

                    // *** CORRECCIÓN CS0023 ***
                    // Intentar obtener DosePerFraction primero
                    if (relevantPlan.DosePerFraction.Dose > 0)
                    {
                        // La API devuelve DosePerFraction en cGy, convertir a Gy
                        dosePerFractionGy = relevantPlan.DosePerFraction.Dose / 100.0;
                        totalDoseGy = dosePerFractionGy * fractions;
                    }
                    // Si no, calcular desde TotalDose (también en cGy)
                    else if (relevantPlan.TotalDose.Dose > 0)
                    {
                        totalDoseGy = relevantPlan.TotalDose.Dose / 100.0;
                        dosePerFractionGy = totalDoseGy / fractions;
                    }
                    // Si ninguno está disponible, no podemos determinar el esquema
                    else
                    {
                        return "Esquema EBRT (Dosis desconocida)";
                    }

                    // Devolver en Gy
                    return $"{totalDoseGy:F1} Gy ({dosePerFractionGy:F2} Gy/fx) en {fractions} fx";
                }
                return "Esquema EBRT no especificado";
            }
            // Para cursos de braquiterapia
            else if (IsBrachyCourse(course.Id))
            {
                var approvedPlans = course.BrachyPlanSetups
                                          .Where(p => IsPlanApproved(p))
                                          .OrderBy(p => p.CreationDateTime) // Ordenar consistentemente
                                          .ToList();

                if (approvedPlans.Any())
                {
                    int fractions = approvedPlans.Count;
                    // *** CORRECCIÓN CS0019 ***
                    // Sumar la dosis por fracción de cada plan (asumiendo que DosePerFraction está en cGy)
                    double totalPrescribedDose_cGy = approvedPlans.Sum(p => p.DosePerFraction.Dose);
                    double totalPrescribedDose_Gy = totalPrescribedDose_cGy / 100.0;

                    // Calcular dosis media por fracción si es necesario
                    double avgDosePerFractionGy = fractions > 0 ? totalPrescribedDose_Gy / fractions : 0;


                    // Devolver en Gy
                    return $"{totalPrescribedDose_Gy:F1} Gy ({avgDosePerFractionGy:F2} Gy/fx) en {fractions} fx";
                }
                return "Esquema HDR-BT no especificado";
            }

            return "Tipo de tratamiento no reconocido";
        }
        //----------------------------------------------------------------------------------------------------------------------
        // Métodos de cálculo de dosis (Sin cambios)
        //----------------------------------------------------------------------------------------------------------------------
        private double CalculateBED(double dosePerFractionInGy, double fractions, double alphaBeta)
        {
            if (alphaBeta == 0) return double.PositiveInfinity; // Evitar división por cero
            return dosePerFractionInGy * fractions * (1 + (dosePerFractionInGy / alphaBeta));
        }

        private double CalculateBEDWithTimeAdjustment(double dosePerFractionInGy, double fractions, double alphaBeta, double totalTreatmentTimeDays, double timeDelayDays, double kRepopFactor)
        {
            if (alphaBeta == 0) return double.PositiveInfinity; // Evitar división por cero
            double bed_no_time = CalculateBED(dosePerFractionInGy, fractions, alphaBeta);
            double timeCorrection = 0;
            // Solo aplicar corrección si el tiempo total excede el retraso
            if (totalTreatmentTimeDays > timeDelayDays)
            {
                // El factor k suele estar en Gy/día, asegurarse que T y Tdelay están en días
                timeCorrection = kRepopFactor * (totalTreatmentTimeDays - timeDelayDays);
            }
            // Asegurar que BED no sea negativo después de la corrección
            return Math.Max(0, bed_no_time - timeCorrection);
        }

        private double CalculateEQD2(double bed, double alphaBeta)
        {
            if (alphaBeta <= 0) return double.NaN; // Incluir alphaBeta=0 y negativos como inválidos para EQD2
            double denominator = (1 + (2.0 / alphaBeta));
            if (Math.Abs(denominator) < 1e-9) return double.PositiveInfinity; // Evitar división por casi cero
            return bed / denominator;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos para obtener dosis en volúmenes (Añadir más robustez)
        //----------------------------------------------------------------------------------------------------------------------
        private double GetDoseAtVolume(PlanSetup plan, string structureId, double volumePercent)
        {
            if (plan?.StructureSet == null) return double.NaN; // Verificar plan y StructureSet
                                                               // Buscar estructura ignorando mayúsculas/minúsculas
            var structure = plan.StructureSet.Structures
                                .FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));

            if (structure == null || structure.IsEmpty || structure.HasSegment == false)
            {
                // Log o mensaje opcional: MessageBox.Show($"Advertencia: Estructura '{structureId}' no encontrada, vacía o sin segmentos en plan '{plan.Id}'.");
                return double.NaN; // Indicar que no se pudo obtener
            }

            // Validar volumePercent
            if (volumePercent < 0 || volumePercent > 100)
            {
                // Log o mensaje opcional: MessageBox.Show($"Advertencia: Porcentaje de volumen '{volumePercent}%' inválido para '{structureId}'.");
                return double.NaN;
            }


            try
            {
                // La API espera porcentaje, devuelve DoseValue (que tiene .Dose en cGy)
                DoseValue doseValue = plan.GetDoseAtVolume(structure, volumePercent, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                return doseValue.Dose; // Devolver valor en cGy
            }
            catch (Exception ex)
            {
                // Capturar excepciones inesperadas de la API
                MessageBox.Show($"Error obteniendo D{volumePercent}% para '{structureId}' en plan '{plan.Id}':\n{ex.Message}", "Error API GetDoseAtVolume");
                return double.NaN;
            }
        }

        private double GetDoseAtVolumeAbsoluta(PlanSetup plan, string structureId, double volumeCC)
        {
            if (plan?.StructureSet == null) return double.NaN;
            var structure = plan.StructureSet.Structures
                                .FirstOrDefault(s => s.Id.Equals(structureId, StringComparison.OrdinalIgnoreCase));

            if (structure == null || structure.IsEmpty || structure.HasSegment == false)
            {
                // Log o mensaje opcional
                return double.NaN;
            }

            // Validar volumeCC
            if (volumeCC < 0)
            {
                // Log o mensaje opcional
                return double.NaN;
            }


            // Asegurarse que el volumen absoluto solicitado no exceda el volumen de la estructura
            if (volumeCC > structure.Volume && structure.Volume > 0)
            {
                // Advertencia opcional
                // MessageBox.Show($"Advertencia: El volumen D{volumeCC:F2}cc solicitado para '{structureId}' excede el volumen total ({structure.Volume:F2}cc). Se usará el volumen total.", "Volumen Excedido");
                volumeCC = structure.Volume; // Usar el volumen total para obtener Dmin efectivo (o D100%)
            }
            // Si el volumen de la estructura es 0, no se puede obtener D(vol>0)
            if (structure.Volume <= 0)
            {
                // Log o mensaje opcional
                return double.NaN;
            }


            try
            {
                // La API espera cc, devuelve DoseValue (con .Dose en cGy)
                DoseValue doseValue = plan.GetDoseAtVolume(structure, volumeCC, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute);
                return doseValue.Dose; // Devolver valor en cGy
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error obteniendo D{volumeCC}cc para '{structureId}' en plan '{plan.Id}':\n{ex.Message}", "Error API GetDoseAtVolumeAbs");
                return double.NaN;
            }
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos para generación de reportes (Sin cambios lógicos mayores)
        //----------------------------------------------------------------------------------------------------------------------
        private void GenerateReportHeader(StringBuilder sb, string patientName, string patientId)
        {
            sb.AppendLine(" RESUMEN DOSIMÉTRICO CONSOLIDADO (EBRT + HDR-BT) - v36"); // Título actualizado
            sb.AppendLine("=====================================================================================");
            sb.AppendLine($" Paciente: {patientName ?? "No disponible"}");
            sb.AppendLine($" ID: {patientId ?? "No disponible"}");
            sb.AppendLine($" Fecha Reporte: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($" α/β Tumor: {alphaBetaTumor} Gy | α/β OAR: {alphaBetaOAR} Gy | Ajuste Tiempo BT: k={k}/día, Tdelay={Tdelay}d");
            sb.AppendLine("-------------------------------------------------------------------------------------");
        }

        private void GenerateTotalSection(StringBuilder sb, Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            sb.AppendLine("\n====================== SECCIÓN TOTAL EQD2 (EBRT + HDR-BT) ======================");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
            sb.AppendLine("| Estructura       | EQD2 Total (Gy)  | Meta         | Límite       | Evaluación Clínica                 |");
            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");

            // Mostrar primero el Target, luego OARs ordenados
            var orderedKeys = eqd2Total.Keys
                                      .OrderBy(k => k.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                      .ThenBy(k => k);

            foreach (var structureKey in orderedKeys)
            {
                double eqd2Val = eqd2Total[structureKey];
                string aimText = "-";
                string limitText = "-";
                string concepto = "Sin restricción definida";

                if (constraints.TryGetValue(structureKey, out var constraint))
                {
                    aimText = constraint.aimText;
                    limitText = constraint.limitText;
                    concepto = EvaluateConstraints(eqd2Val, constraint);
                }

                // Formatear el valor EQD2, mostrando "N/A" si es NaN
                string eqd2Display = double.IsNaN(eqd2Val) ? "N/A" : $"{eqd2Val:F2}";

                sb.AppendLine($"│ {structureKey,-15} │ {eqd2Display,16} │ {aimText,-10} │ {limitText,-10} │ {concepto,-30} │");
            }

            // Añadir estructuras con constraints que no tuvieran valor calculado (EQD2=0 o NaN)
            foreach (var constraintPair in constraints.OrderBy(kv => kv.Key))
            {
                if (!eqd2Total.ContainsKey(constraintPair.Key))
                {
                    string concepto = EvaluateConstraints(double.NaN, constraintPair.Value); // Evaluar con NaN si falta
                    sb.AppendLine($"│ {constraintPair.Key,-15} │ {"N/A",16} │ {constraintPair.Value.aimText,-10} │ {constraintPair.Value.limitText,-10} │ {concepto,-30} │");
                }
            }


            sb.AppendLine("-----------------------------------------------------------------------------------------------------------");
        }

        private string EvaluateConstraints(double eqd2Val, (double aimValue, double limitValue, string type, string aimText, string limitText) constraint)
        {
            double aimVal = constraint.aimValue;
            double limitVal = constraint.limitValue;
            string tipo = constraint.type;

            if (double.IsNaN(eqd2Val)) return "? Dato EQD2 no disponible"; // Caso de NaN explícito

            if (tipo == "lessThan") // Para OARs
            {
                if (eqd2Val <= aimVal)
                    return "✔ OK (Cumple meta)";
                else if (eqd2Val <= limitVal)
                    return "⚠ OK (Dentro límite)";
                else
                    return "✖ FALLO (Supera límite)";
            }
            else if (tipo == "range") // Para Target (PTV/CTV)
            {
                if (eqd2Val >= aimVal && eqd2Val <= limitVal)
                    return "✔ OK (Rango óptimo)";
                else if (eqd2Val < aimVal)
                    return "⚠ FALLO (Por debajo meta)"; // Considerar si esto es fallo o advertencia
                else // eqd2Val > limitVal
                    return "✖ FALLO (Excede límite)";
            }
            return "? Tipo restricción no definido";
        }

        private void EvaluateTreatmentPlan(StringBuilder sb, Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            sb.AppendLine("\n====================== EVALUACIÓN FINAL DEL PLAN ======================");
            bool isApprovedOverall = IsTreatmentPlanApproved(eqd2Total, constraints);
            string finalMessage;
            if (isApprovedOverall)
            {
                finalMessage = "👍 ESTADO GLOBAL: APROBADO. Todos los criterios evaluados cumplen.";
            }
            else
            {
                finalMessage = "✋ ESTADO GLOBAL: NO APROBADO. Revisar estructuras con ⚠ o ✖.";
            }
            sb.AppendLine(finalMessage);
            sb.AppendLine("=======================================================================");
        }

        private bool IsTreatmentPlanApproved(Dictionary<string, double> eqd2Total,
            Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            // Iterar sobre las restricciones definidas, no sobre los resultados calculados
            // Esto asegura que se evalúen todas las restricciones obligatorias
            foreach (var constraintPair in constraints)
            {
                string structureKey = constraintPair.Key;
                var constraintData = constraintPair.Value;
                double eqd2Val = double.NaN; // Asumir no disponible por defecto

                if (eqd2Total.ContainsKey(structureKey))
                {
                    eqd2Val = eqd2Total[structureKey];
                }

                string evaluacion = EvaluateConstraints(eqd2Val, constraintData);

                // Definir qué evaluaciones causan un fallo general
                if (evaluacion.Contains("FALLO") || evaluacion.Contains("?")) // Fallo si supera límite, está por debajo, o falta dato
                {
                    return false; // El plan falla si CUALQUIER restricción falla o falta
                }
                // "⚠ OK (Dentro límite)" se considera APROBADO aquí.
            }

            // Si se evaluaron todas las restricciones y ninguna falló
            return true;
        }

        //----------------------------------------------------------------------------------------------------------------------
        // Métodos auxiliares (Sin cambios lógicos)
        //----------------------------------------------------------------------------------------------------------------------
        private bool IsPlanApproved(PlanSetup plan)
        {
            if (plan == null) return false;
            // Considera qué status de aprobación son válidos para incluir en el cálculo
            return plan.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved ||
                   plan.ApprovalStatus == PlanSetupApprovalStatus.Completed ||
                   plan.ApprovalStatus == PlanSetupApprovalStatus.CompletedEarly;
            // Excluir: PlanningApproved, Rejected, Retired, UnApproved, Reviewed
        }

        private void SaveAsTxt(StringBuilder sb)
        {
            string patientId = "ID_NoDisp";
            // Intentar extraer ID del reporte de forma segura
            try
            {
                string idLine = sb.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                                    .FirstOrDefault(line => line.Trim().StartsWith("ID:"));
                if (idLine != null)
                {
                    patientId = idLine.Split(':').LastOrDefault()?.Trim() ?? patientId;
                    // Remover caracteres inválidos para nombres de archivo
                    foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
                    {
                        patientId = patientId.Replace(invalidChar, '_');
                    }
                }
            }
            catch { /* Ignorar error de parsing */ }


            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo de texto|*.txt",
                Title = "Guardar reporte como TXT",
                FileName = $"Reporte_Consolidado_{patientId}_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveDialog.FileName, sb.ToString());
                    MessageBox.Show("Reporte guardado como TXT exitosamente.", "Éxito",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar el archivo:\n{ex.Message}", "Error de Guardado",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        //----------------------------------------------------------------------------------------------------------------------
        // *** NUEVO: Métodos de generación de gráficos y reporte mejorados ***
        //----------------------------------------------------------------------------------------------------------------------

        // *** CORRECCIÓN CS0103 ***
        // Añadir parámetros eqd2Total y constraints
        private void ShowEnhancedReportWindow(StringBuilder sb, Dictionary<string, List<double>> btDosesPerFraction,
                                              Dictionary<string, double> eqd2Total,
                                              Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            // El resto del método permanece igual que en la versión anterior,
            // pero ahora tiene acceso a eqd2Total y constraints para pasarlos a CreateSummaryPanel.

            var window = new Window
            {
                Title = "Resumen Dosimétrico Avanzado - V36",
                Width = 1400,
                Height = 950,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
            };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tabControl = new TabControl { Background = Brushes.WhiteSmoke, BorderBrush = Brushes.LightGray, Margin = new Thickness(10) };

            // Pestaña Texto
            var textTab = new TabItem { Header = CreateTabHeader("📝 Reporte", Brushes.SteelBlue), Background = Brushes.Transparent };
            var textBlock = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Padding = new Thickness(20), TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
            ProcessEnhancedColoredText(sb.ToString(), textBlock); // Usar el formateador mejorado
            var scrollViewer = new ScrollViewer { Content = textBlock, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(5) };
            textTab.Content = new Border { Child = scrollViewer, Background = Brushes.White, CornerRadius = new CornerRadius(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(5) };
            tabControl.Items.Add(textTab);

            // Pestaña Gráficos BT
            if (btDosesPerFraction != null && btDosesPerFraction.Any(kvp => kvp.Value.Any()))
            {
                var chartTab = new TabItem { Header = CreateTabHeader("📊 Gráficos BT", Brushes.DarkSeaGreen), Background = Brushes.Transparent };
                var chartGrid = new Grid();
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Gráfico Dosis Física BT
                var doseChartContainer = new GroupBox { Header = "Dosis Física por Fracción (Braquiterapia)", Style = GetGroupBoxStyle(), Margin = new Thickness(10) };
                var doseChart = CreateEnhancedBarChart(btDosesPerFraction, false, "Dosis Física [Gy]"); // isTotalEQD2 = false
                doseChartContainer.Content = doseChart;
                Grid.SetColumn(doseChartContainer, 0);
                chartGrid.Children.Add(doseChartContainer);

                // Gráfico EQD2 Total BT
                var eqd2ChartContainer = new GroupBox { Header = "EQD2 Total por Estructura (Solo Braquiterapia)", Style = GetGroupBoxStyle(), Margin = new Thickness(10) };
                var btTotalEQD2Data = CalculateTotalEQD2Values(btDosesPerFraction); // Calcular EQD2 solo de BT
                var eqd2ChartData = btTotalEQD2Data.ToDictionary(kvp => kvp.Key, kvp => new List<double> { kvp.Value }); // Adaptar formato para la función chart
                var eqd2Chart = CreateEnhancedBarChart(eqd2ChartData, true, "EQD2 Total [Gy]"); // isTotalEQD2 = true
                eqd2ChartContainer.Content = eqd2Chart;
                Grid.SetColumn(eqd2ChartContainer, 1);
                chartGrid.Children.Add(eqd2ChartContainer);

                chartTab.Content = chartGrid;
                tabControl.Items.Add(chartTab);
            }

            // Pestaña Resumen Clave
            var summaryTab = new TabItem { Header = CreateTabHeader("📋 Resumen Clave", Brushes.LightSteelBlue), Background = Brushes.Transparent };
            // *** CORRECCIÓN CS0103: Pasar eqd2Total y constraints ***
            var summaryPanel = CreateSummaryPanel(sb.ToString(), eqd2Total, constraints);
            summaryTab.Content = new ScrollViewer { Content = summaryPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) };
            tabControl.Items.Add(summaryTab);

            Grid.SetRow(tabControl, 0);
            mainGrid.Children.Add(tabControl);

            // Panel Botones
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10), Background = Brushes.Transparent };
            var btnTxt = new Button { Content = "📤 Exportar a TXT", Margin = new Thickness(5), Style = CreateButtonStyle(Brushes.SteelBlue) };
            btnTxt.Click += (s, e) => SaveAsTxt(sb);
            var btnPrint = new Button { Content = "🖨️ Imprimir Reporte", Margin = new Thickness(5), Style = CreateButtonStyle(Brushes.DarkSeaGreen) };
            btnPrint.Click += (s, e) => PrintReport(sb.ToString());
            buttonPanel.Children.Add(btnTxt);
            buttonPanel.Children.Add(btnPrint);
            Grid.SetRow(buttonPanel, 1);
            mainGrid.Children.Add(buttonPanel);

            window.Content = mainGrid;
            window.ShowDialog();
        }


        // Método para crear el panel de resumen (Sin cambios lógicos, recibe params)
        private FrameworkElement CreateSummaryPanel(string reportText, Dictionary<string, double> eqd2Total, Dictionary<string, (double aimValue, double limitValue, string type, string aimText, string limitText)> constraints)
        {
            var mainPanel = new StackPanel { Margin = new Thickness(20) };

            // Tarjeta de Información del Paciente
            var patientInfoBox = new GroupBox
            {
                Header = "Información del Paciente",
                Margin = new Thickness(0, 0, 0, 15),
                Style = GetGroupBoxStyle()
            };
            var patientInfoPanel = new StackPanel { Margin = new Thickness(10) };
            // Extraer info del header del reportText de forma segura
            var headerLines = reportText.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Take(6); // Tomar algunas líneas del inicio
            foreach (var line in headerLines)
            {
                // Buscar líneas relevantes por palabras clave
                if (line.Contains("Paciente:") || line.Contains("ID:") || line.Contains("α/β") || line.Contains("Fecha Reporte"))
                {
                    patientInfoPanel.Children.Add(new TextBlock { Text = line.Trim(), Margin = new Thickness(0, 2, 0, 2), FontSize = 13 });
                }
            }
            patientInfoBox.Content = patientInfoPanel;
            mainPanel.Children.Add(patientInfoBox);


            // Tarjeta de Resumen de Dosis Totales EQD2 y Evaluación
            var totalSummaryBox = new GroupBox
            {
                Header = "Resumen EQD2 Total y Cumplimiento",
                Margin = new Thickness(0, 0, 0, 15),
                Style = GetGroupBoxStyle()
            };

            var summaryGrid = new Grid { Margin = new Thickness(10) };
            // Definir columnas
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // Estructura (más ancha)
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // Valor EQD2
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });   // Evaluación (más ancha)
            summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header

            // Header de la tabla de resumen
            summaryGrid.Children.Add(CreateSummaryHeader("Estructura", 0));
            summaryGrid.Children.Add(CreateSummaryHeader("EQD2 Total (Gy)", 1));
            summaryGrid.Children.Add(CreateSummaryHeader("Evaluación", 2));

            int rowIndex = 1;
            // Iterar sobre las restricciones para asegurar que todas se muestran
            foreach (var constraintPair in constraints.OrderBy(kv => kv.Key.Equals("PTV+CTV", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(kv => kv.Key))
            {
                summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string structureKey = constraintPair.Key;
                var constraintData = constraintPair.Value;
                double eqd2Val = double.NaN; // Asumir NaN por defecto
                Brush evalColor = Brushes.DarkViolet; // Color para '?'

                if (eqd2Total.TryGetValue(structureKey, out double foundEqd2))
                {
                    eqd2Val = foundEqd2;
                }

                string evaluation = EvaluateConstraints(eqd2Val, constraintData);
                // Determinar color basado en la evaluación
                if (evaluation.Contains("✔")) evalColor = Brushes.DarkGreen;
                else if (evaluation.Contains("⚠ OK")) evalColor = Brushes.DarkOrange; // Para advertencias que pasan
                else if (evaluation.Contains("FALLO")) evalColor = Brushes.DarkRed; // Para fallos
                                                                                    // else: Mantiene el color violeta para '?'

                // Mostrar "N/A" si EQD2 es NaN
                string eqd2Display = double.IsNaN(eqd2Val) ? "N/A" : $"{eqd2Val:F2}";

                // *** CORRECCIÓN CS1736: Pasar FontWeight explícitamente ***
                summaryGrid.Children.Add(CreateSummaryCell(structureKey, 0, rowIndex, FontWeights.SemiBold)); // Peso para nombre estructura
                summaryGrid.Children.Add(CreateSummaryCell(eqd2Display, 1, rowIndex, FontWeights.Normal)); // Peso normal para valor
                summaryGrid.Children.Add(CreateSummaryCell(evaluation, 2, rowIndex, FontWeights.Normal, evalColor)); // Peso normal para evaluación

                rowIndex++;
            }

            totalSummaryBox.Content = summaryGrid;
            mainPanel.Children.Add(totalSummaryBox);

            // Tarjeta de Evaluación Final
            var finalEvalBox = new GroupBox
            {
                Header = "Evaluación Final del Plan",
                Margin = new Thickness(0, 0, 0, 15),
                Style = GetGroupBoxStyle()
            };
            var finalEvalText = new TextBlock { Margin = new Thickness(15), FontSize = 14, TextWrapping = TextWrapping.Wrap };
            bool isApproved = IsTreatmentPlanApproved(eqd2Total, constraints); // Re-evaluar aquí
            if (isApproved)
            {
                finalEvalText.Text = "👍 ESTADO GLOBAL: APROBADO. Todos los criterios evaluados cumplen.";
                finalEvalText.Foreground = Brushes.DarkGreen;
                finalEvalText.FontWeight = FontWeights.Bold;
            }
            else
            {
                finalEvalText.Text = "✋ ESTADO GLOBAL: NO APROBADO. Revisar estructuras con FALLO o datos faltantes (?).";
                finalEvalText.Foreground = Brushes.DarkRed;
                finalEvalText.FontWeight = FontWeights.Bold;
            }
            finalEvalBox.Content = finalEvalText;
            mainPanel.Children.Add(finalEvalBox);

            return mainPanel;
        }


        // Helper para celdas de resumen
        // *** CORRECCIÓN CS1736: Removido valor por defecto para weight ***
        private TextBlock CreateSummaryCell(string text, int column, int row, FontWeight weight, Brush foreground = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = (column == 1) ? HorizontalAlignment.Right : HorizontalAlignment.Left, // Alinear valor a la derecha
                FontWeight = weight, // Usar el peso pasado
                Foreground = foreground ?? Brushes.Black
            };
            Grid.SetColumn(tb, column);
            Grid.SetRow(tb, row);
            return tb;
        }

        // Helper para cabeceras de resumen (Sin cambios)
        private Border CreateSummaryHeader(string text, int column)
        {
            var border = new Border
            {
                Background = Brushes.LightSteelBlue,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 2), // Pequeño margen inferior
                Child = new TextBlock { Text = text, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
            };
            Grid.SetColumn(border, column);
            Grid.SetRow(border, 0);
            return border;
        }

        // Helper para estilo de GroupBox
        private Style GetGroupBoxStyle()
        {
            var style = new Style(typeof(GroupBox));
            style.Setters.Add(new Setter(GroupBox.BackgroundProperty, Brushes.White));
            style.Setters.Add(new Setter(GroupBox.BorderBrushProperty, Brushes.LightGray));
            style.Setters.Add(new Setter(GroupBox.ForegroundProperty, Brushes.SteelBlue));
            style.Setters.Add(new Setter(GroupBox.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(GroupBox.PaddingProperty, new Thickness(5)));
            style.Setters.Add(new Setter(GroupBox.BorderThicknessProperty, new Thickness(1)));
            // *** CORRECCIÓN CS0117: Removida la línea de CornerRadiusProperty ***
            // style.Setters.Add(new Setter(GroupBox.CornerRadiusProperty, new CornerRadius(5))); // GroupBox no tiene esta propiedad
            return style;
        }


        // Método para crear los gráficos de barras mejorados (Sin cambios lógicos)
        private FrameworkElement CreateEnhancedBarChart(Dictionary<string, List<double>> data, bool isTotalEQD2, string yAxisLabel)
        {
            const double margin = 60;
            const double barWidth = 35;
            const double spacing = 15;
            const double chartHeight = 350;
            double chartWidth = margin * 2;

            if (isTotalEQD2)
            {
                chartWidth += data.Count * (barWidth + spacing) - spacing;
            }
            else
            {
                int maxFractions = data.Values.Max(l => l?.Count ?? 0);
                double groupWidth = data.Count * (barWidth + spacing) - spacing;
                chartWidth += maxFractions * groupWidth + (maxFractions > 0 ? (maxFractions - 1) * spacing * 3 : 0);
            }
            chartWidth = Math.Max(chartWidth, 500);

            var canvas = new Canvas { Width = chartWidth, Height = chartHeight, Background = Brushes.WhiteSmoke, SnapsToDevicePixels = true };

            // Calcular valor máximo
            double maxValue = 0;
            if (data != null && data.Any())
            {
                maxValue = data.Values.SelectMany(l => l ?? Enumerable.Empty<double>()) // Manejar listas null
                                 .Where(d => !double.IsNaN(d)) // Ignorar NaN
                                 .DefaultIfEmpty(0) // Evitar error si todo es NaN
                                 .Max();
            }
            if (maxValue <= 0) maxValue = 10; // Valor por defecto si max es 0 o negativo
                                              // Redondear al múltiplo de 5 superior, asegurando que no sea 0
            maxValue = Math.Max(5, Math.Ceiling(maxValue / 5.0) * 5);


            // Ejes
            var xAxis = new Line { X1 = margin, Y1 = chartHeight - margin, X2 = chartWidth - margin / 2, Y2 = chartHeight - margin, Stroke = Brushes.Gray, StrokeThickness = 1 };
            var yAxis = new Line { X1 = margin, Y1 = margin / 2, X2 = margin, Y2 = chartHeight - margin, Stroke = Brushes.Gray, StrokeThickness = 1 };
            canvas.Children.Add(xAxis);
            canvas.Children.Add(yAxis);
            var yAxisTitle = new TextBlock { Text = yAxisLabel, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, LayoutTransform = new RotateTransform(-90) };
            Canvas.SetLeft(yAxisTitle, margin / 2 - 10);
            Canvas.SetTop(yAxisTitle, (chartHeight - margin - margin / 2) / 2 + margin / 2); // Centrar mejor
            canvas.Children.Add(yAxisTitle);

            // Marcas Eje Y y Rejilla
            int numTicks = 5;
            double availableHeight = chartHeight - margin - margin / 2;
            for (int i = 0; i <= numTicks; i++)
            {
                double yValue = (maxValue / numTicks) * i;
                double yPos = chartHeight - margin - (yValue / maxValue * availableHeight);
                var gridLine = new Line { X1 = margin, Y1 = yPos, X2 = chartWidth - margin / 2, Y2 = yPos, Stroke = Brushes.LightGray, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 2, 2 } };
                canvas.Children.Add(gridLine);
                var tick = new Line { X1 = margin - 5, Y1 = yPos, X2 = margin, Y2 = yPos, Stroke = Brushes.Gray, StrokeThickness = 1 };
                var label = new TextBlock { Text = yValue.ToString("F1"), FontSize = 10, Foreground = Brushes.DimGray };
                Canvas.SetLeft(label, margin - 45); // Ajustar posición etiqueta Y
                Canvas.SetTop(label, yPos - 8);
                canvas.Children.Add(tick);
                canvas.Children.Add(label);
            }

            // Barras
            if (isTotalEQD2)
            { // Gráfico Totales
                double xPos = margin + spacing * 2;
                foreach (var item in data.OrderBy(kv => kv.Key))
                {
                    double value = item.Value?.FirstOrDefault() ?? double.NaN; // Manejar lista null/vacía
                    double barHeight = double.IsNaN(value) ? 0 : (value / maxValue * availableHeight);
                    if (barHeight < 0) barHeight = 0;

                    var bar = new Rectangle { Width = barWidth, Height = barHeight, Fill = GetEnhancedStructureColor(item.Key), Stroke = Brushes.DimGray, StrokeThickness = 0.5, ToolTip = $"{item.Key}: {(double.IsNaN(value) ? "N/A" : value.ToString("F2"))}" };
                    Canvas.SetLeft(bar, xPos);
                    Canvas.SetTop(bar, chartHeight - margin - barHeight);
                    var label = new TextBlock { Text = item.Key, FontSize = 10, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, Width = barWidth + spacing };
                    Canvas.SetLeft(label, xPos - spacing / 2);
                    Canvas.SetTop(label, chartHeight - margin + 5);
                    canvas.Children.Add(bar);
                    canvas.Children.Add(label);
                    if (barHeight > 20 && !double.IsNaN(value))
                    {
                        var valueLabel = new TextBlock { Text = value.ToString("F2"), FontSize = 9, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, Width = barWidth };
                        Canvas.SetLeft(valueLabel, xPos);
                        Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 15);
                        canvas.Children.Add(valueLabel);
                    }
                    xPos += barWidth + spacing;
                }
            }
            else
            { // Gráfico por Fracciones
                int maxFractions = data.Values.Max(l => l?.Count ?? 0);
                double groupSpacing = spacing * 3;
                double xStart = margin + groupSpacing;
                for (int i = 0; i < maxFractions; i++)
                {
                    double groupWidth = data.Count * (barWidth + spacing) - spacing;
                    double xPosGroupStart = xStart + i * (groupWidth + groupSpacing);
                    var fracLabel = new TextBlock { Text = $"Fx {i + 1}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, Width = groupWidth };
                    Canvas.SetLeft(fracLabel, xPosGroupStart);
                    Canvas.SetTop(fracLabel, chartHeight - margin + 5);
                    canvas.Children.Add(fracLabel);
                    double xPosInGroup = xPosGroupStart;
                    foreach (var kvp in data.OrderBy(kv => kv.Key))
                    {
                        double value = double.NaN;
                        if (kvp.Value != null && i < kvp.Value.Count)
                        {
                            value = kvp.Value[i];
                        }
                        double barHeight = double.IsNaN(value) ? 0 : (value / maxValue * availableHeight);
                        if (barHeight < 0) barHeight = 0;

                        var bar = new Rectangle { Width = barWidth, Height = barHeight, Fill = GetEnhancedStructureColor(kvp.Key), Stroke = Brushes.DimGray, StrokeThickness = 0.5, ToolTip = $"{kvp.Key} - Fx {i + 1}: {(double.IsNaN(value) ? "N/A" : value.ToString("F2"))}" };
                        Canvas.SetLeft(bar, xPosInGroup);
                        Canvas.SetTop(bar, chartHeight - margin - barHeight);
                        canvas.Children.Add(bar);
                        if (barHeight > 15 && !double.IsNaN(value))
                        {
                            var valueLabel = new TextBlock { Text = value.ToString("F1"), FontSize = 8, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, Width = barWidth };
                            Canvas.SetLeft(valueLabel, xPosInGroup);
                            Canvas.SetTop(valueLabel, chartHeight - margin - barHeight - 12);
                            canvas.Children.Add(valueLabel);
                        }
                        xPosInGroup += barWidth + spacing;
                    }
                }
            }

            // Leyenda
            var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            if (data != null)
            {
                foreach (var item in data.OrderBy(kv => kv.Key))
                {
                    var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 15, 0) };
                    legendItem.Children.Add(new Rectangle { Width = 12, Height = 12, Fill = GetEnhancedStructureColor(item.Key), Stroke = Brushes.DimGray, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 5, 0) });
                    legendItem.Children.Add(new TextBlock { Text = item.Key, FontSize = 10, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center });
                    legend.Children.Add(legendItem);
                }
            }

            var chartContainer = new StackPanel { Orientation = Orientation.Vertical };
            chartContainer.Children.Add(canvas);
            chartContainer.Children.Add(legend);

            return new ScrollViewer { Content = chartContainer, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5) };
        }


        // Método para obtener colores mejorados para las estructuras (Sin cambios)
        private Brush GetEnhancedStructureColor(string structureId)
        {
            var normalizedId = structureId?.Replace("_HDR", "").Replace("-HDR", "") ?? ""; // Manejar null
            var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
             {
                 {"HR-CTV", Color.FromRgb(65, 105, 225)},
                 {"PTV+CTV", Color.FromRgb(65, 105, 225)},
                 {"Recto", Color.FromRgb(60, 179, 113)},
                 {"Vejiga", Color.FromRgb(218, 112, 214)},
                 {"Sigma", Color.FromRgb(255, 140, 0)}
             };
            return colors.TryGetValue(normalizedId, out var color) ? new SolidColorBrush(color) : new SolidColorBrush(Color.FromRgb(169, 169, 169));
        }

        // Método para crear cabeceras de pestaña estilizadas (Sin cambios)
        private StackPanel CreateTabHeader(string text, Brush color)
        {
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = text, Margin = new Thickness(5), Foreground = color, FontWeight = FontWeights.SemiBold, FontSize = 13 } } };
        }

        // Método para crear un estilo reutilizable para botones (Sin cambios)
        private Style CreateButtonStyle(Brush background)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, background));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(15, 8, 15, 8)));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.DarkGray));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Button.MinWidthProperty, 140.0));
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
            border.AppendChild(contentPresenter);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            var mouseOverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            var originalColor = ((SolidColorBrush)background).Color;
            var darkerColor = Color.FromArgb(originalColor.A, (byte)(originalColor.R * 0.85), (byte)(originalColor.G * 0.85), (byte)(originalColor.B * 0.85));
            mouseOverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(darkerColor)));
            mouseOverTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Gray));
            style.Triggers.Add(mouseOverTrigger);
            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            var darkestColor = Color.FromArgb(originalColor.A, (byte)(originalColor.R * 0.7), (byte)(originalColor.G * 0.7), (byte)(originalColor.B * 0.7));
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(darkestColor)));
            pressedTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Black));
            style.Triggers.Add(pressedTrigger);
            return style;
        }


        // Método para procesar el texto con colores mejorados (Sin cambios lógicos)
        private void ProcessEnhancedColoredText(string text, TextBlock textBlock)
        {
            var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            textBlock.Inlines.Clear();
            foreach (var line in lines)
            {
                var run = new Run(line + (line.EndsWith(Environment.NewLine) ? "" : Environment.NewLine)) { Foreground = Brushes.DimGray, FontFamily = new FontFamily("Consolas") };
                if (line.Trim().StartsWith("RESUMEN DOSIMÉTRICO") || line.Trim().StartsWith("EVALUACIÓN FINAL")) { run.FontFamily = new FontFamily("Segoe UI"); run.FontSize = 16; run.FontWeight = FontWeights.Bold; run.Foreground = Brushes.Navy; }
                else if (line.Contains("Paciente:") || line.Contains("ID:") || line.Contains("α/β") || line.Contains("Fecha Reporte")) { run.FontFamily = new FontFamily("Segoe UI"); run.FontSize = 13; run.Foreground = Brushes.Black; }
                else if (line.Contains("✔ OK")) { run.Foreground = Brushes.DarkGreen; run.Background = new SolidColorBrush(Color.FromArgb(50, 144, 238, 144)); run.FontWeight = FontWeights.Bold; run.FontFamily = new FontFamily("Segoe UI Semibold"); }
                else if (line.Contains("⚠ OK")) { run.Foreground = Brushes.DarkOrange; run.Background = new SolidColorBrush(Color.FromArgb(50, 255, 215, 0)); run.FontWeight = FontWeights.Bold; run.FontFamily = new FontFamily("Segoe UI Semibold"); }
                else if (line.Contains("FALLO")) { run.Foreground = Brushes.DarkRed; run.Background = new SolidColorBrush(Color.FromArgb(50, 255, 182, 193)); run.FontWeight = FontWeights.Bold; run.FontFamily = new FontFamily("Segoe UI Semibold"); }
                else if (line.Contains("?")) { run.Foreground = Brushes.DarkViolet; run.Background = Brushes.LightGray; run.FontWeight = FontWeights.Bold; run.FontFamily = new FontFamily("Segoe UI Semibold"); }
                else if (line.Contains("=== SECCIÓN EBRT ===") || line.Contains("=== SECCIÓN HDR-BT ===")) { run.Foreground = Brushes.White; run.Background = Brushes.SteelBlue; run.FontWeight = FontWeights.Bold; run.FontSize = 14; run.FontFamily = new FontFamily("Segoe UI"); }
                else if (line.Contains("=== SECCIÓN TOTAL ===")) { run.Foreground = Brushes.White; run.Background = Brushes.DarkSlateBlue; run.FontWeight = FontWeights.Bold; run.FontSize = 14; run.FontFamily = new FontFamily("Segoe UI"); }
                else if (line.Contains("|") && (line.Contains("Estructura") || line.Contains("Métrica"))) { run.FontWeight = FontWeights.SemiBold; run.Foreground = Brushes.Black; run.Background = Brushes.LightGray; }
                else if (line.Contains("---") || line.Contains("===")) { run.Foreground = Brushes.LightGray; }
                else if (line.Trim().StartsWith("│") || line.Trim().StartsWith("|")) { run.Foreground = Brushes.Black; run.FontSize = 12; }
                textBlock.Inlines.Add(run);
            }
        }

        // Método para imprimir el reporte (Sin cambios lógicos)
        private void PrintReport(string reportText)
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                var document = new FlowDocument { PageWidth = printDialog.PrintableAreaWidth, PageHeight = printDialog.PrintableAreaHeight, PagePadding = new Thickness(50), ColumnGap = 0, ColumnWidth = printDialog.PrintableAreaWidth, FontFamily = new FontFamily("Consolas"), FontSize = 10 };
                var formattedTextBlock = new TextBlock();
                ProcessEnhancedColoredText(reportText, formattedTextBlock);
                var paragraph = new Paragraph();
                while (formattedTextBlock.Inlines.Count > 0) { var inline = formattedTextBlock.Inlines.FirstInline; formattedTextBlock.Inlines.Remove(inline); paragraph.Inlines.Add(inline); }
                document.Blocks.Add(paragraph);
                try { printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"Reporte Dosimétrico {DateTime.Now:yyyy-MM-dd}"); }
                catch (Exception ex) { MessageBox.Show($"Error al imprimir el reporte:\n{ex.Message}", "Error de Impresión", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        //----------------------------------------------------------------------------------------------------------------------
        // *** NUEVO: Métodos auxiliares adicionales para UI y cálculos ***
        //----------------------------------------------------------------------------------------------------------------------
        // Método auxiliar para calcular EQD2 Total SOLO de Braquiterapia (Sin cambios lógicos)
        private Dictionary<string, double> CalculateTotalEQD2Values(Dictionary<string, List<double>> btDosesPerFractionPhys)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // Claves deben mapear los IDs de estructura usados en btDosesPerFractionPhys a los alpha/beta
            var alphaBetaValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
             {
                 {"HR-CTV", alphaBetaTumor},
                 {"Recto_HDR", alphaBetaOAR},
                 {"Vejiga_HDR", alphaBetaOAR},
                 {"Sigma_HDR", alphaBetaOAR}
                 // Añade aquí si hay otras estructuras específicas de BT con sus alpha/beta
             };

            if (btDosesPerFractionPhys == null) return result; // Devolver vacío si no hay datos

            foreach (var kvp in btDosesPerFractionPhys)
            {
                double totalEQD2 = 0;
                string structureKey = kvp.Key;

                if (alphaBetaValues.TryGetValue(structureKey, out double alphaBeta) && kvp.Value != null)
                {
                    foreach (var doseGy in kvp.Value) // Asumiendo que btDosesPerFractionPhys tiene dosis en Gy
                    {
                        // Solo calcular si la dosis es válida y positiva
                        if (!double.IsNaN(doseGy) && doseGy > 0)
                        {
                            double bed = CalculateBEDWithTimeAdjustment(doseGy, 1, alphaBeta, totalTime, Tdelay, k);
                            totalEQD2 += CalculateEQD2(bed, alphaBeta);
                        }
                    }
                }
                else
                {
                    // Log o advertencia si no se encuentra alpha/beta o la lista de dosis es null
                }
                result.Add(structureKey, totalEQD2);
            }
            return result;
        }

    } // Fin de la clase Script
} // Fin del namespace VMS.TPS