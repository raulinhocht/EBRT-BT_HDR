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
        //CODE 4 - Nombres de curso y plan EBRT y BT ------------------------------------------------------------------------------------------

        // Iterar a través de los dispositivos de soporte del paciente
        foreach (var device in context.PlanSetup.Course.ExternalPlanSetups)
        {
            MessageBox.Show($"  Course: {device.Course}"); //nombre del curso
            MessageBox.Show($"  Plan: {device.Id}"); //nombre del plan
            MessageBox.Show($"  Plan type: {device.PlanType}"); //ExternalBeam
            MessageBox.Show($"  Fractions: {device.NumberOfFractions}"); //n fx
            MessageBox.Show($"  DosePerFraction: {device.DosePerFraction}"); //dose n cGy
        }

        // Iterar a través de los dispositivos de soporte del paciente
        foreach (var device in context.PlanSetup.Course.BrachyPlanSetups)
        {
            MessageBox.Show($"  Course: {device.Course}"); //nombre del curso
            MessageBox.Show($"  Plan: {device.Id}"); //nombre del plan
            MessageBox.Show($"  Plan type: {device.PlanType}"); //ExternalBeam
            MessageBox.Show($"  Fractions: {device.NumberOfFractions}"); //n fx
            MessageBox.Show($"  DosePerFraction: {device.DosePerFraction}"); //dose n cGy
        }

        }
    }
}
