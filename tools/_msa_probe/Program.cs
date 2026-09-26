using System.Reflection;
using System.Runtime.Loader;
var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".nuget\packages\cmllib.core\4.0.6\lib\net8.0\CmlLib.Core.dll");
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
var t = asm.GetType("CmlLib.Core.Auth.MSession")!;
foreach (var c in t.GetConstructors()) Console.WriteLine(c);
foreach (var p in t.GetProperties()) Console.WriteLine($"prop {p.PropertyType.Name} {p.Name}");
foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine(m);
