using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Input;


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
            // TODO : Add here the code that is called when the script is launched from Eclipse.

            /*string planId = "1758980"; 
            //ExternalPlanSetup EBRT = context.Course.ExternalPlanSetups.FirstOrDefault(p => p.Name == "1758980");
            Course Curso = context.Course.Id("1758980");
            if (EBRT == null)
            {
                Console.WriteLine($"El plan no se encontró.");
                return;
            }
            */




            //CODE 1 -------------------------------------------------------------------------------------------
            /*
            Structure PTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_CMI");
            
            DVHData dvhData = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
            DVHPoint pt95 = new DVHPoint();

            if (dvhData == null)
            {
                MessageBox.Show("No hay datos");
            }
            else
                pt95 = dvhData.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == 2262);
                MessageBox.Show("Dosis de radiación para: " + pt95.Volume.ToString("F4"));
            
            */
            //CODE 2 -------------------------------------------------------------------------------------------
            /*
            //Structure PTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_CMI");
            Structure CTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");
            int doseBT = 1614;

            //DVHData dvhData_EBRT = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
            DVHData dvhData_BT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

            //DVHPoint pt95_EBRT = new DVHPoint();
            DVHPoint pt95_BT = new DVHPoint();

            if (/*dvhData_EBRT == null &&*//* dvhData_BT == null)
            {
                MessageBox.Show("No hay datos");
            }
            else
                //pt95_EBRT = dvhData_EBRT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == 2680); //V100
                pt95_BT = dvhData_BT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == doseBT); //V10

                MessageBox.Show("Volumen 2" + pt95_BT.Volume.ToString("F3"));
            */

            //CODE 3 -------------------------------------------------------------------------------------------
            /*
            Course course = context.Patient.Courses.FirstOrDefault(c => c.Id == "EBRT");

            if (course == null)
            {
                MessageBox.Show("No hay datos");
            }
            else
                MessageBox.Show("Curso ENCONTRADO: " + course.Id);

            */
            //CODE 4 -------------------------------------------------------------------------------------------

            /*Patient course = context.PlanSetup.Course.FirstOrDefault(p => p.Id == "EBRT");

            if (plan == null)
            {
                MessageBox.Show("No hay datos Plan");
            }
            else
                MessageBox.Show("Plan ENCONTRADO: " + plan.Id);
            */

            /*
            // Verifica si hay un plan cargado
            // Obtener el curso asociado al plan
            var course = context.PlanSetup.Course;
            if (course == null)
            {
                Console.WriteLine("No se encontró un curso asociado al plan.");
                return;
            }

            // Iterar a través de los dispositivos de soporte del paciente
            foreach (var device in context.PlanSetup.Course.ExternalPlanSetups)
            {
                MessageBox.Show($"  Course: {device.Course}"); //nombre del curso
                MessageBox.Show($"  Dispositivo: {device.Id}"); //nombre del plan
                MessageBox.Show($"  Beams: {device.PlanType}"); //ExternalBeam
                MessageBox.Show($"  Course: {device.NumberOfFractions}"); //n fx
                MessageBox.Show($"  DosePerFraction: {device.DosePerFraction}"); //dose n cGy
            }
            */

            //CODE 5 -------------------------------------------------------------------------------------------
            /*
            foreach (Course device in context.Patient.Courses)
            {
                MessageBox.Show($"  Course: {device.Id}"); //nombre del curso

                foreach (var device1 in context.PlanSetup.Course.ExternalPlanSetups)
                {
                MessageBox.Show($"Plan EBRT: {device1.Id}"); //nombre del plan
                }
                
                foreach (var device2 in context.PlanSetup.Course.BrachyPlanSetups)
                {
                    MessageBox.Show($"Plan BT: {device2.Id}"); //nombre del plan
                }

            }
            */
            /*
            //CODE 6 -------------------------------------------------------------------------------------------
            var curso = context.PlanSetup.Course;
            var plan = context.PlanSetup;

            if (curso != null)
            {
                MessageBox.Show($"Curso: {curso.Id}"); // Nombre del curso
                MessageBox.Show($"Plan EBRT: {context.PlanSetup.Id}"); // Nombre del plan
            }
            else
            {
                MessageBox.Show("NE curso.");
            }

            if (plan == null)
            {
                MessageBox.Show("NE plan");
            }
            */


            //CODE 7 -------------------------------------------------------------------------------------------20241216
            /*
            foreach (Course curso in context.Patient.Courses)
            {
                if (curso.Id == "1. Cervix")
                {
                    MessageBox.Show("jhhhh");
                    //--------------var plan = context.PlanSetup.Course.ExternalPlanSetups.FirstOrDefault(p => p.Id == "Cervix_56Gy");
                    var planext = curso.ExternalPlanSetups.FirstOrDefault(p => p.Id == "Cervix_56Gy");
                    MessageBox.Show($"Plan encontrado External RT: {planext.Id}"); //nombre del plan en EBRT


                }

                else if (curso.Id == "2. Fletcher")
                {
                    foreach (var device2 in context.PlanSetup.Course.BrachyPlanSetups)
                    {
                        MessageBox.Show($"Plan BT: {device2.Id}"); //nombre del plan
                    }
                }
                else
                    MessageBox.Show("ninguno");
            }
            */


            //CODE 8 -------------------------------------------------------------------------------------------20241217
            /*
            foreach (Course curso in context.Patient.Courses)
            {
                // Validar curso "CURSO EBRT"
                if (curso.Id == "1. Cervix")
                {
                    MessageBox.Show("curso: EBRT");
                    var planext = curso.ExternalPlanSetups.FirstOrDefault(p => p.Id == "Cervix_56Gy");
                    MessageBox.Show($"Plan encontrado EBRT: {planext.Id}"); //nombre del plan en EBRT

                    // Obtener DVH para las estructuras
                    Structure PTV = context.StructureSet.Structures.FirstOrDefault(xP => xP.Id == "PTV_56");
                    Structure Recto = context.StructureSet.Structures.FirstOrDefault(xR => xR.Id == "Recto");
                    Structure Vejiga = context.StructureSet.Structures.FirstOrDefault(xV => xV.Id == "Vejiga");
                    Structure Sigma = context.StructureSet.Structures.FirstOrDefault(xS => xS.Id == "Sigma");
                    DVHData dvhDataP = context.PlanSetup.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataR = context.PlanSetup.GetDVHCumulativeData(Recto, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataV = context.PlanSetup.GetDVHCumulativeData(Vejiga, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    DVHData dvhDataS = context.PlanSetup.GetDVHCumulativeData(Sigma, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                    
                    if (dvhDataP == null)
                    {
                        MessageBox.Show("NE datos en HDV");
                    }

                    // Buscar punto en DVH
                    double dosisP = 5567;
                    double dosisR = 1000;
                    double dosisV = 1000;
                    double dosisS = 1000;
                    DVHPoint P90 = dvhDataP.CurveData.FirstOrDefault(ptoP => ptoP.DoseValue.Dose == dosisP);
                    DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => ptoR.DoseValue.Dose == dosisR);
                    DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => ptoV.DoseValue.Dose == dosisV);
                    DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => ptoS.DoseValue.Dose == dosisS);

                    if (P90.Volume > 0)
                    {
                        MessageBox.Show($"Volumen para dosis {dosisP}: {P90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisR}: {R90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisV}: {V90.Volume:F4} cm³ \n" +
                            $" Volumen para dosis {dosisS}: {S90.Volume:F4} cm³");
                    }
                    else
                    {
                        MessageBox.Show("NE volumen");
                    }

                }

                // Validar curso "CURSO BT"
                else if (curso.Id == "2. Fletcher")
                {
                    MessageBox.Show("curso: BT");
                    foreach (var planbt in context.PlanSetup.Course.BrachyPlanSetups)
                    {
                        MessageBox.Show($"Plan encontrado BT: {planbt.Id}"); //nombre del plan BT
                                                
                        // Obtener DVH para la estructura CTV
                        //Structure CTV = context.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");
                        //DVHData dvhDataBT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        //planbt in context.PlanSetup.Course.BrachyPlanSetups
                        Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");

                        DVHData dvhDataBT = context.PlanSetup.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);
                        MessageBox.Show($"Volumen {CTV.Volume}");

                        if (dvhDataBT == null)
                        {
                            MessageBox.Show("NE datos en HDV");
                        }

                        // Buscar punto en DVH
                        double dosisBT = 579;
                        DVHPoint C90BT = dvhDataBT.CurveData.FirstOrDefault(pto => pto.DoseValue.Dose == dosisBT);


                        if (C90BT.Volume > 0)
                        {
                            MessageBox.Show($"Volumen para dosis {dosisBT}: {C90BT.Volume:F4} cm³");
                        }
                        else
                        {
                            MessageBox.Show($"NE volumen");
                        }
                        
                        
                    }
                }
                else
                    MessageBox.Show("ninguno");
            */
            //CODE 9 -------------------------------------------------------------------------------------------20241223 OK
            /*
            foreach (Course curso in context.Patient.Courses)
            {
                if (curso.Id == "1. Cervix") {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        MessageBox.Show($"Course: {curso.Id}"); //nombre del curso
                        MessageBox.Show($"Plan EBRT: {planext.Id}"); //nombre del plan

                        Structure PTV = planext.StructureSet.Structures.FirstOrDefault(x => x.Id == "PTV_56");

                        // Obtener los datos del DVH
                        DVHData dvhDataE = planext.GetDVHCumulativeData(PTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        if (dvhDataE != null)
                        {
                            // Buscar punto en el DVH
                            double dosisE = 579; // Dosis
                            DVHPoint P90E = dvhDataE.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisE) < 0.01);
                            DVHPoint? P90E0 = dvhDataE.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisE) < 0.01);//------------------------------------------------------------

                            if (P90E0 != null)
                            {
                                MessageBox.Show($"Dosis: {dosisE} cGy \nVolumen correspondiente: {P90E.Volume:F2} cm³");
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
                    foreach (var planbt in curso.BrachyPlanSetups) // Accede a planes BT
                    {
                        MessageBox.Show($"Course: {curso.Id}"); // Nombre del curso asociado
                        MessageBox.Show($"Plan BT: {planbt.Id}"); // Nombre del plan de braquiterapia

                        // Obtener la estructura HR-CTV
                        Structure CTV = planbt.StructureSet.Structures.FirstOrDefault(x => x.Id == "HR-CTV");

                        // Obtener los datos del DVH
                        DVHData dvhDataBT = planbt.GetDVHCumulativeData(CTV, DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, 1);

                        if (dvhDataBT != null)
                        {
                            // Buscar punto en el DVH
                            double dosisBT = 579; // Dosis
                            DVHPoint C90BT = dvhDataBT.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisBT) < 0.01);
                            DVHPoint? C90BT0 = dvhDataBT.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisBT) < 0.01);//------------------------------------------------------------

                            if (C90BT0 != null)
                            {
                                MessageBox.Show($"Dosis: {dosisBT} cGy \nVolumen correspondiente: {C90BT.Volume:F2} cm³");
                            }
                        }
                        else
                        {
                            MessageBox.Show("No hay datos disponibles");
                        }
                    }
                }

            }
            */
            //CODE 10 -------------------------------------------------------------------------------------------20241217
            /*
            foreach (Course curso in context.Patient.Courses)
            {
                if (curso.Id == "1. Cervix")
                {
                    foreach (var planext in curso.ExternalPlanSetups)
                    {
                        MessageBox.Show($"Course: {curso.Id}"); //nombre del curso
                        MessageBox.Show($"Plan EBRT: {planext.Id}"); //nombre del plan

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
                            double dosisP = 5500; // Dosis PTV Ref 95.85
                            double dosisR = 1500; // Dosis RECTO Ref 45.36
                            double dosisV = 1100; // Dosis VEJIGA Ref 282.54
                            double dosisS = 2200; // Dosis SIGMA Ref 35.5
                            DVHPoint? P90E0 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);//****************************************
                            DVHPoint P90 = dvhDataP.CurveData.FirstOrDefault(ptoP => Math.Abs(ptoP.DoseValue.Dose - dosisP) < 0.01);
                            DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                            DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                            DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                            if (P90E0 != null)
                            {
                                MessageBox.Show(
                                    $"PTV----- Dosis: {dosisP} cGy, Volumen: {P90.Volume:F2} cm³\n" +
                                    $"RECTO----- Dosis: {dosisR} cGy, Volumen: {R90.Volume:F2} cm³\n" +
                                    $"VEJIGA----- Dosis: {dosisV} cGy, Volumen: {V90.Volume:F2} cm³\n" +
                                    $"SIGMA----- Dosis: {dosisS} cGy, Volumen: {S90.Volume:F2} cm³\n");
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
                        MessageBox.Show($"Course: {curso.Id}"); // Nombre del curso asociado
                        MessageBox.Show($"Plan BT: {planbt.Id}"); // Nombre del plan de braquiterapia

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
                            double dosisC = 510; // Dosis CTV Ref 46
                            double dosisR = 50; // Dosis RECTO Ref 60.9
                            double dosisV = 80; // Dosis VEJIGA Ref 237.2
                            double dosisS = 40; // Dosis SIGMA Ref 9.86
                            DVHPoint? C90BT0 = dvhDataC.CurveData.FirstOrDefault(pto => Math.Abs(pto.DoseValue.Dose - dosisC) < 0.01);//****************************************
                            DVHPoint C90 = dvhDataC.CurveData.FirstOrDefault(ptoC => Math.Abs(ptoC.DoseValue.Dose - dosisC) < 0.01);
                            DVHPoint R90 = dvhDataR.CurveData.FirstOrDefault(ptoR => Math.Abs(ptoR.DoseValue.Dose - dosisR) < 0.01);
                            DVHPoint V90 = dvhDataV.CurveData.FirstOrDefault(ptoV => Math.Abs(ptoV.DoseValue.Dose - dosisV) < 0.01);
                            DVHPoint S90 = dvhDataS.CurveData.FirstOrDefault(ptoS => Math.Abs(ptoS.DoseValue.Dose - dosisS) < 0.01);

                            if (C90BT0 != null)
                            {
                                MessageBox.Show(
                                    $"PTV----- Dosis: {dosisC} cGy, Volumen: {C90.Volume:F2} cm³\n" +
                                    $"RECTO----- Dosis: {dosisR} cGy, Volumen: {R90.Volume:F2} cm³\n" +
                                    $"VEJIGA----- Dosis: {dosisV} cGy, Volumen: {V90.Volume:F2} cm³\n" +
                                    $"SIGMA----- Dosis: {dosisS} cGy, Volumen: {S90.Volume:F2} cm³\n");
                            }
                        }
                        else
                        {
                            MessageBox.Show("No hay datos disponibles");
                        }
                    }
                }
            }
            */
            //CODE 11 -------------------------------------------------------------------------------------------20241217
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
                            double BED_P = dosisP * 0.01 * ( 1+ ((dosisP * 0.01 /fracciones_EBRT)/alphaBeta_10));
                            double BED_R = dosisR * 0.01 * ( 1+ ((dosisR * 0.01 / fracciones_EBRT) / alphaBeta_3));
                            double BED_V = dosisV * 0.01 * ( 1+ ((dosisV * 0.01 / fracciones_EBRT) / alphaBeta_3));
                            double BED_S = dosisS * 0.01 * ( 1+ ((dosisS * 0.01 / fracciones_EBRT) / alphaBeta_3));

                            //EQD2
                            EQD2_P = BED_P / (1 + (2/alphaBeta_10));
                            EQD2_V = BED_V / (1 + (2/alphaBeta_3));
                            EQD2_R = BED_R / (1 + (2/alphaBeta_3));
                            EQD2_S = BED_S / (1 + (2/alphaBeta_3));

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