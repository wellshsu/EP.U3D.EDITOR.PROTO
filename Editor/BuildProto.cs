//---------------------------------------------------------------------//
//                    GNU GENERAL PUBLIC LICENSE                       //
//                       Version 2, June 1991                          //
//                                                                     //
// Copyright (C) Wells Hsu, wellshsu@outlook.com, All rights reserved. //
// Everyone is permitted to copy and distribute verbatim copies        //
// of this license document, but changing it is not allowed.           //
//                  SEE LICENSE.md FOR MORE DETAILS.                   //
//---------------------------------------------------------------------//
using EP.U3D.EDITOR.BASE;
using Google.Protobuf.Reflection;
using ProtoBuf.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;

namespace EP.U3D.EDITOR.PROTO
{
    public class BuildProto
    {
        public static Type WorkerType = typeof(BuildProto);

        [MenuItem(Constants.MENU_PATCH_BUILD_PROTO)]
        public static void Invoke()
        {
            var worker = Activator.CreateInstance(WorkerType) as BuildProto;
            worker.Process();
        }

        public virtual void Process()
        {
            var pkg = Helper.FindPackage(Assembly.GetExecutingAssembly());
            List<string> files = new List<string>();
            Helper.CollectFiles(Constants.PROTO_SRC_PATH, files);
            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file).ToUpper();
                if (file.EndsWith(".h"))
                {
#if EFRAME_CS
                    Header2CS(file, Constants.PROTO_CS_PATH, name, false);
#endif
#if EFRAME_ILR
                    Header2CS(file, Constants.PROTO_ILR_PATH, name, true);
#endif
#if EFRAME_LUA
                    Header2LUA(file, Constants.PROTO_LUA_PATH, name);
#endif
                }
                else if (file.EndsWith(".proto"))
                {
#if EFRAME_CS
                    Proto2CS(pkg.resolvedPath, Constants.PROTO_SRC_PATH, name, file, Constants.PROTO_CS_PATH, false);
#endif
#if EFRAME_ILR
                    Proto2CS(pkg.resolvedPath, Constants.PROTO_SRC_PATH, name, file, Constants.PROTO_ILR_PATH, true);
#endif
#if EFRAME_LUA
                    Proto2LUA(pkg.resolvedPath, Constants.PROTO_SRC_PATH, name, file, Constants.PROTO_LUA_PATH);
#endif
                }
            }
            AssetDatabase.Refresh();
            string toast = "Build proto done.";
            Helper.Log(toast);
            Helper.ShowToast(toast);
        }

        public virtual void Header2CS(string header, string dst, string name, bool ilr)
        {
            string ctt = "// AUTO GENERATED, DO NOT EDIT //\n";
            ctt += $"namespace {(ilr ? "ILRProto" : "CSProto")}\n";
            ctt += "{";
            string[] lines = File.ReadAllLines(header);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("enum ")) line = line.Replace("enum ", "public enum ");
                ctt += $"\t{line}\n";
            }
            ctt += "}";
            Helper.SaveText($"{dst}{name}.cs", ctt);
        }

        public virtual void Header2LUA(string header, string dst, string name)
        {
            if (File.Exists(header) == false) return;
            string lua = $"{dst}{name}.lua";
            if (File.Exists(lua)) File.Delete(lua);
            if (!Helper.HasDirectory(Path.GetDirectoryName(lua))) Helper.CreateDirectory(Path.GetDirectoryName(lua));
            using (var destFile = File.Open(lua, FileMode.Create))
            {
                StreamWriter sw = new StreamWriter(destFile);
                sw.WriteLine("-- AUTO GENERATED, DO NOT EDIT --");

                using (var srcFile = File.OpenText(header))
                {
                    int lastEnumIndex = -1;
                    bool beginParse = false;
                    while (srcFile.EndOfStream == false)
                    {
                        string line = srcFile.ReadLine();
                        if (line.StartsWith("enum"))
                        {
                            if (beginParse)
                            {
                                sw.WriteLine("}");
                                sw.WriteLine();
                            }

                            string structName = line.Replace("\t", "");
                            structName = structName.Replace(" ", "");
                            structName = structName.Replace("{", "");
                            int index = structName.IndexOf('/');
                            if (index > 0)
                            {
                                structName = structName.Substring(0, index);
                            }
                            structName = structName.Replace("/", "");
                            structName = structName.Substring(4, structName.Length - 4);
                            sw.WriteLine(structName + " = {");
                            beginParse = true;
                            lastEnumIndex = -1;// reset enum value.
                            continue;
                        }

                        if (line.StartsWith("/") || string.IsNullOrEmpty(line)
                            || line.StartsWith("{") || line.StartsWith("}")
                            || line.Replace(" ", "").StartsWith("*") || line.Replace(" ", "").StartsWith("/")
                            || beginParse == false)
                        {
                            continue;
                        }
                        string messageName = string.Empty;
                        messageName = line;
                        messageName = messageName.Replace("\t", "");
                        messageName = messageName.Replace(" ", "");
                        if (string.IsNullOrEmpty(messageName))
                        {
                            continue;
                        }
                        int index1 = messageName.IndexOf('/');
                        if (index1 == 0)
                        {
                            continue;
                        }
                        if (index1 > 0)
                        {
                            messageName = messageName.Substring(0, index1);
                        }
                        messageName = messageName.Replace("/", "");

                        int enumIndex;

                        int index2 = messageName.IndexOf("=");
                        int index3 = messageName.IndexOf(",");
                        if (index2 > 0)
                        {
                            string enumIndexStr = messageName.Substring(index2 + 1, index3 - index2 - 1);
                            enumIndexStr = enumIndexStr.Replace(" ", "");
                            try
                            {
                                enumIndex = int.Parse(enumIndexStr);
                                messageName = messageName.Substring(0, index2);
                            }
                            catch
                            {
                                continue; // ref enum value.
                            }
                        }
                        else
                        {
                            enumIndex = lastEnumIndex + 1;
                        }
                        lastEnumIndex = enumIndex;
                        messageName = messageName.Replace(",", "");
                        string[] comments = line.Split(new string[] { "//" }, StringSplitOptions.RemoveEmptyEntries);
                        if (comments.Length >= 2)
                        {
                            sw.WriteLine("\t--- " + comments[1].TrimStart());
                            sw.WriteLine("\t" + messageName + " = " + enumIndex + ",");
                        }
                        else
                        {
                            sw.WriteLine("\t" + messageName + " = " + enumIndex + ",");
                        }
                    }
                    srcFile.Close();
                }
                sw.WriteLine("}");
                sw.Close();
            }
        }

        public virtual void Proto2CS(string env, string root, string name, string proto, string dst, bool ilr)
        {
            string tmp = $"{root}/{name}.tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            List<string> nlines = new List<string>();
            string[] lines = File.ReadAllLines(proto);
            bool syntax = false;
            for (int j = 0; j < lines.Length; j++)
            {
                string line = lines[j];
                if (!syntax && (line.Contains("syntax =") || line.Contains("syntax="))) syntax = true;
                if (!line.Contains("import ") &&
                    !line.Contains("package "))
                {
                    nlines.Add(line);
                }
            }
            if (!syntax) nlines.Insert(0, "syntax = \"proto2\";");
            using (var file = File.Open(tmp, FileMode.CreateNew))
            {
                StreamWriter writer = new StreamWriter(file);
                foreach (var line in nlines)
                {
                    writer.WriteLine(line);
                }
                writer.WriteLine();
                writer.Close();
                file.Close();
            }
            var set = new FileDescriptorSet();
            set.AddImportPath(root);
            set.Add(Path.GetFileName(tmp), true);
            set.Files[0].Package = ilr ? $"ILRProto.{name}" : $"CSProto.{name}";
            set.Process();
            var errors = set.GetErrors();
            if (errors.Length > 0)
            {
                for (int i = 0; i < errors.Length; i++)
                {
                    var e = errors[i];
                    Helper.LogError(e.Message);
                }
            }
            CSharpCodeGenerator.ClearTypeNames();
            var files = CSharpCodeGenerator.Default.Generate(set);
            foreach (var file in files)
            {
                var path = Path.Combine(dst, file.Name);
                File.WriteAllText(path, file.Text);
            }
            Helper.DeleteFile(tmp);
        }

        public virtual void Proto2LUA(string env, string root, string name, string proto, string dst)
        {
            string tmp = $"{root}/{name}.tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var file = File.Open(tmp, FileMode.CreateNew))
            {
                StreamWriter writer = new StreamWriter(file);
                string[] lines = File.ReadAllLines(proto);
                for (int j = 0; j < lines.Length; j++)
                {
                    string line = lines[j];
                    if (!line.Contains("import ") &&
                        !line.Contains("package "))
                    {
                        writer.WriteLine(line);
                    }
                }
                writer.WriteLine();
                writer.Close();
                file.Close();
            }
            // 使用相对路径时，必须设置proto_path，否则无法定位proto文件的位置。
            Process cmd = new Process();
            cmd.StartInfo.FileName = env + "/Editor/Libs/Protoc/ForLUA/build.bat";
            string arg = Helper.StringFormat("{0} {1} {2}", dst, root, tmp);
            cmd.StartInfo.WorkingDirectory = env + "/Editor/Libs/Protoc/ForLUA/";
            cmd.StartInfo.Arguments = arg;
            cmd.Start();
            cmd.WaitForExit();
            cmd.Close();
            Helper.DeleteFile(tmp);
            string dstf = Path.Combine(dst, name + ".lua");
            string ctt = Helper.OpenText(dstf);
            ctt = ctt.Replace("#MODULE_NAME#", "Gen.Proto." + name);
            Helper.SaveText(dstf, ctt);
        }

        class NamespaceNormalizer : NameNormalizer
        {
            public override string Pluralize(string identifier)
            {
                throw new NotImplementedException();
            }

            protected override string GetName(string identifier)
            {
                throw new NotImplementedException();
            }
        }

    }
}