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
        //CODE 6 - Impresion PL y CS estático ----------------------------------------------------------------------------
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

        }
    }
}
