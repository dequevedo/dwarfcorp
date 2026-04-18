using System;
using System.Collections.Generic;
using System.Reflection;

namespace DwarfCorp
{
    // CodeDOM-based runtime C# compilation was removed when migrating to .NET 10.
    // .cs files inside mods are now ignored. To restore this, rewrite using
    // Microsoft.CodeAnalysis.CSharp (Roslyn). See TODO_LIST.md.
    public static class ModCompiler
    {
        public static Assembly CompileCode(IEnumerable<String> Files)
        {
            Console.Out.WriteLine("ModCompiler: runtime .cs compilation is disabled on .NET 10+ (pending Roslyn port). Skipping.");
            return null;
        }
    }
}
