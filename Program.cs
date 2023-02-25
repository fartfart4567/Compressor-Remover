using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using HarmonyLib;

namespace CompressorRemover {
    internal class Program {
        
        private static string Path;
        private static string Output;
        public static void Main(string[] args) {
            Path = args[0];
            Output = Path.Contains(".exe")
                ? Path.Replace(".exe", "_noCompressor.exe")
                : Path.Replace(".dll", "_noCompressor.dll");
            ModuleDefMD _module = ModuleDefMD.Load(args[0]);
            if (isCompressor(_module)) {
                Console.WriteLine("Compressor Detected");
                Console.WriteLine("Attempting to grab module...");
                var harmony = new Harmony("compressorRemover");
                var assembly = Assembly.LoadFile(args[0]);
                harmony.PatchAll(typeof(Program).Assembly);
                var entrypoint = assembly.EntryPoint;
                var parameters = entrypoint.GetParameters();
                entrypoint.Invoke(null, new object[parameters.Length]);
                return;
            }
            Console.WriteLine("Couldn't detect compressor");
            Thread.Sleep(1500);
            Environment.Exit(-1);
        }

        static bool isCompressor(ModuleDefMD module) {
            var entryPoint = module.EntryPoint;
            var globalType = module.GlobalType;
            int detections = 0;

            if (entryPoint.DeclaringType == globalType)
                detections++;

            var instrs = entryPoint.Body.Instructions;

            if (entryPoint.HasBody) {
                if (instrs[0].IsLdcI4()) {
                    if (instrs[1].OpCode == OpCodes.Pop) {
                        if (instrs[2].OpCode == OpCodes.Newarr) {
                            if (instrs[2].Operand.ToString().Contains("Uint32"))
                                detections++;
                            if (entryPoint.ReturnType == module.CorLibTypes.Int32)
                                detections++;
                        }
                    }
                }

                var firstStr = instrs.First(x => x.OpCode == OpCodes.Ldstr);
                if (firstStr.Operand.ToString() == "koi") detections++;
            }

            return detections > 0;
        }

        static bool fixEntrypoint(ModuleDefMD Module, int token) {
            if (token > 0) {
                var realEntry = Module.ResolveToken(token);
                var entryMethod = Module.ResolveMethod(realEntry.Rid);
                Module.EntryPoint = entryMethod;
                Module.Write(Output.Replace("noCompressor", "NoCompressor-FixedEntrypoint"), new ModuleWriterOptions(Module) {
                    Logger = DummyLogger.NoThrowInstance
                });
                return true;
            }

            return false;
        }
        

        [HarmonyPatch(typeof(Assembly), nameof(Assembly.LoadModule), new []{ typeof(string), typeof(byte[])})]
        class ModulePatch {
            static bool Prefix(ref Module __result, string moduleName, byte[] rawModule) {
                Console.WriteLine("Grabbed Module");
                File.WriteAllBytes(Output, rawModule);
                return true;
            }
        }

        [HarmonyPatch(typeof(Module), nameof(Module.ResolveMethod), new Type[] {typeof(int)})]
        class GetMethodPatch {
            static bool Prefix(ref MethodBase __result, int metadataToken) {
                var fixedEntrypoint = fixEntrypoint(ModuleDefMD.Load(Output), metadataToken);
                Console.WriteLine(fixedEntrypoint ? "Fixed Entrypoint" : "Couldn't fix entrypoint");
                //File.Delete(Output);
                Thread.Sleep(1500);
                Console.WriteLine("Saved in : " + Output);
                Environment.Exit(0);
                return false;
            }
        }
    }
}