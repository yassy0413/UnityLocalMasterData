using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using LocalMasterData.SourceGenerator;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var runtime = new[] { "ILocalMasterDataTable.cs", "LocalMasterDataTable.cs", "LocalMasterDataResolver.cs", "LocalMasterDataTableRegistration.cs", "LocalMasterDataAttribute.cs" };
var sample = """
using System;
using System.IO;
using System.Collections.Generic;
namespace JetBrains.Annotations { public class PublicAPIAttribute : Attribute {} }
namespace UnityEngine {
 public enum RuntimeInitializeLoadType { SubsystemRegistration }
 public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
 public static class Debug { public static void Log(object x) {} public static void LogError(object x) { throw new Exception(x.ToString()); } }
}
namespace LocalMasterData {
 public class LocalMasterDataReader {
  public static LocalMasterDataReader Instance = new(); public bool IsLoaded = true;
  public Dictionary<Type, ILocalMasterDataTable> Tables = new();
  public static void RegisterTableFactory(string name, Func<IReadOnlyList<LocalMasterDataTableRegistration>> factory) {}
 }
}
public struct Custom { public int Value; }
public class BaseRecord { public string Name { get; set; } = ""; }
[LocalMasterData.LocalMasterData("Item")]
public sealed partial class Item : LocalMasterData.LocalMasterDataTable<int, Item.Record, Item> {
 public class Record : BaseRecord {
  public int Id { get; set; }
  public short Small { get; set; }
  public ushort Unsigned { get; set; }
  public long Large { get; set; }
  public uint U32 { get; set; }
  public ulong U64 { get; set; }
  public float F { get; set; }
  public double D { get; set; }
  public bool B { get; set; }
  public DateTime Date { get; set; }
  public Custom Custom { get; set; }
 }
 public override int GetKey(Record r) => r.Id;
}
public static class Check {
 public static void Run() {
  LocalMasterData.LocalMasterDataResolver.Register<Custom>(s => new Custom { Value = int.Parse(s) }, (w,v) => w.Write(v.Value), r => new Custom { Value = r.ReadInt32() });
  var t = new Item();
  var rows = new List<Dictionary<string,string>> { new() {
   ["Id"]="42", ["Name"]="日本語", ["Small"]="-12", ["Unsigned"]="65000", ["Large"]="-12345678901", ["U32"]="4000000000", ["U64"]="18000000000000000000", ["F"]="1.25", ["D"]="2.5", ["B"]="true", ["Date"]="2026-11-12T00:00:00Z", ["Custom"]="71"
  }};
  var bytes = t.CreateBinary(rows);
  t.ReadBinary(bytes);
  var v = t.GetRecord(42);
  if (v.Name != "日本語" || v.Small != -12 || v.Unsigned != 65000 || v.Large != -12345678901 || v.U32 != 4000000000 || v.U64 != 18000000000000000000 || v.F != 1.25f || v.D != 2.5 || !v.B || v.Date != DateTime.Parse("2026-11-12T00:00:00Z").ToUniversalTime() || v.Custom.Value != 71) throw new Exception("round trip mismatch");
  t.Dispose();
  LocalMasterData.LocalMasterDataResolver.Register<Custom>(s => new Custom { Value = int.Parse(s) }, (w,v) => w.Write(v.Value), r => new Custom { Value = r.ReadInt32() + 1 });
  t.ReadBinary(bytes);
  if (t.GetRecord(42).Custom.Value != 72) throw new Exception("resolver refresh failed");
 }
}
""";
var options = new CSharpParseOptions(LanguageVersion.CSharp9);
var trees = runtime.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, f)), options)).Append(CSharpSyntaxTree.ParseText(sample, options));
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("ReaderFixture", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LocalMasterDataSourceGenerator() }, parseOptions: options);
driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
using var stream = new MemoryStream();
var emit = output.Emit(stream);
if (!emit.Success || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) throw new Exception(string.Join("\n", emit.Diagnostics.Concat(diagnostics)));
var asm = Assembly.Load(stream.ToArray());
asm.GetType("__LocalMasterDataGeneratedRegistry")!.GetMethod("CreateTables", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
asm.GetType("Check")!.GetMethod("Run")!.Invoke(null, null);
var bad = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("[LocalMasterData.LocalMasterData(\"Bad\")] public class Bad : LocalMasterData.LocalMasterDataTable<int, Bad.R, Bad> { public class R { public int Id { get; private set; } } public override int GetKey(R r) => r.Id; }", options));
driver.RunGeneratorsAndUpdateCompilation(bad, out _, out var badDiagnostics);
if (!badDiagnostics.Any(d => d.Id == "LMD004")) throw new Exception("Missing invalid setter diagnostic");
Console.WriteLine("PASS: generated reader compilation, all supported primitive types, inherited property, custom struct resolver, resolver replacement, invalid setter diagnostic.");
