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
        //CODE 5 - Impresion cursos y planes a partir de cualquier curso ----------------------------------------

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
    }
  }
}
