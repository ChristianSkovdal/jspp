using SuperModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jspp
{
    class Program
    {
        const string Version = "1.0.0";

        static string[] directories;
        static string filemask;
        //static string[] exclude;

        const string commentPrefix = "//";
        const string symprefix = "#";
        const string symboldef = commentPrefix + symprefix + "DEFINE ";

        class Symbol
        {
            public Symbol(string expr)
            {
                var startPara = expr.IndexOf('(');
                if (startPara<0)
                    throw new Exception("Start paranthesis was not found");
                Name=expr.Substring(0,startPara);
                var endPara = expr.LastIndexOf(")");
                var arglist = expr.Substring(startPara+1, endPara-startPara-1);
                foreach (var arg in arglist.Split(','))
                {
                    this.Arguments.Add(arg);
                }
            }
            public string Name;
            public List<string> Arguments = new List<string>();
        }

        static int Main(string[] args)
        {
            int res = ParseArguments(args);

            Console.WriteLine("jspp v. " + Version + "\nBy Christian Skovdal Andersen, 2014.\n" +
                              "A simple preprocessor for pretty much any file\n");
            if (res > 0)
                return 1;

            int errorIdx = 0;
            try
            {

                var allFiles = new List<string>();
                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                        throw new Exception("The directory '"+dir+"' is not valid");

                    foreach (string file in Directory.EnumerateFiles(dir, filemask, SearchOption.AllDirectories))
                    {
                        allFiles.Add(file);
                    }
                }


                Console.WriteLine("Processing "+allFiles.Count()+" files");
                int progress=0;
                if (allFiles.Count() >= 1000)
                    progress = allFiles.Count() / 100;
                var fileLines = new Dictionary<string, List<string>>();

                int fileIdx=0, percentage=0;
                foreach (var file in allFiles)
                {
                    if (progress>0 && fileIdx++ % progress == 0)
                        Console.WriteLine(++percentage + "% done");
                    var lines = File.ReadAllLines(file);
                    fileLines.Add(file, lines.ToList());
                    var symbols = lines.Select(r => r.Trim()).Where(r => r.StartsWith(symboldef));
                    var symnames = new Dictionary<string, Symbol>();
                    foreach (var sym in symbols)
                    {
                        try
                        {
                            var toks = sym.Split(' ');

                            // This is a define in the form 
                            // #DEFINE symbol content
                            if (toks.Length > 2)
                            {
                                // symbol end with a closed paranthesis
                                var endPar = toks[1].IndexOf(")");
                                if (endPar < 0)
                                    throw new Exception("Opening paranthesis in symbol not found");
                                var symexpr = toks[1].Substring(0, endPar + 1);
                                var startPar = symexpr.IndexOf("(");
                                if (startPar < 0)
                                    throw new Exception("Closing paranthesis in symbol not found");
                                var symname = symexpr.Substring(0, startPar);

                                // It should be a function like this symbol(arg1,arg2,arg3)                            
                                symnames.Add(symname, new Symbol(toks[2]));
                                Console.WriteLine("Adding symbol '" + symname + "'");
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Critical error: " + ex.Message + " while processing symbol '"+sym+"' in file '"+file+"'");
                        }
                    }

                    foreach (var entry in fileLines)
                    {
                        string fn = entry.Key;
                        bool modified = false;

                        for (int i = 0; i < entry.Value.Count(); i++)
                        {
                            string line = entry.Value[i].Trim();
                            foreach (var sym in symnames.Keys)
                            {
                                var symIdx = line.IndexOf(sym);
                                if (symIdx >= 0 && !line.StartsWith(commentPrefix))
                                {
                                    // Is the symbol standalone?
                                    Func<char, bool> IsAllowedLetter = c => (c == '_');

                                    char before = symIdx - 1 < 0 ? ' ' : line[symIdx - 1];
                                    char after = line[symIdx + sym.Length];

                                    if (!Char.IsLetterOrDigit(before) &&
                                        !Char.IsLetterOrDigit(after) &&
                                        !IsAllowedLetter(before) &&
                                        !IsAllowedLetter(after))
                                    {

                                        var newline = InsertSymbol(sym, line, symnames[sym], ref errorIdx, i+1, fn);
                                        entry.Value[i] = newline;
                                        modified = true;
                                    }
                                }
                            }

                        }

                        if (modified)
                        {
                            Console.WriteLine("Processed '" + Path.GetFileName(fn) + "'");

                            File.WriteAllLines(fn, entry.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }



            return 0;
        }

        const string symbolIndex = "__INDEX__";
        const string symbolFile = "__FILE__";
        const string symbolFilePath = "__FILEPATH__";
        const string symbolLine = "__LINE__";
        const string symbolArg0 = "__ARG0__";
        const string symbolArg1 = "__ARG1__";
        const string symbolArg2 = "__ARG2__";
        const string symbolArg3 = "__ARG3__";


        static string Extract(string line, char startChar, char endChar)
        {
            var start = line.IndexOf(startChar);
            var end = line.LastIndexOf(endChar);
            if (start < 0 || end < 0)
                throw new Exception("The line '"+line+"' is not a valid symbol");
            return line.Substring(start+1,end-start-1);
        }

        private static string InsertSymbol(string fn, string line, Symbol sym, ref int errorIdx, int lineIdx, string filename)
        {
            try
            {
                // break up the line
                var argumentList = Extract(line, '(', ')');
                var args = argumentList.Split(','); //line.Split('(')[1].Split(')')[0].Split(',');
                if (args.Length > sym.Arguments.Count())
                    throw new Exception("Argument list does not match symbol");

                int start = line.IndexOf(fn);
                int end = line.LastIndexOf(")");

                string nl = line.Substring(0, start);
                nl += sym.Name +"(";

                var replaced = new List<string>();
                for (int i = 0; i < sym.Arguments.Count(); i++)
                {
                    var arg = sym.Arguments[i];
                    if (arg==symbolIndex)
                    {
                        errorIdx++;
                    }
                    arg = arg.Replace(symbolIndex, errorIdx.ToString());
                    arg = arg.Replace(symbolFile, "'" + Path.GetFileName(filename) + "'");
                    arg = arg.Replace(symbolFilePath, "'" + filename + "'");
                    arg = arg.Replace(symbolLine, lineIdx.ToString());

                    if (args.Length > 0)
                        arg = arg.Replace(symbolArg0, "'" + args[0] + "'");
                    if (args.Length > 1)
                        arg = arg.Replace(symbolArg1, "'" + args[1] + "'");
                    if (args.Length > 2)
                        arg = arg.Replace(symbolArg2, "'" + args[2] + "'");
                    if (args.Length > 3)
                        arg = arg.Replace(symbolArg3, "'" + args[3] + "'");



                    replaced.Add(arg);
                }

                var segment = new ArraySegment<string>(replaced.ToArray(), args.Length, sym.Arguments.Count() - args.Length);
                var arglist = args.Concat(segment);
                var fnargs = string.Join(",", arglist);
                
                nl +=  fnargs;
                nl += line.Substring(end, line.Length-end);

                return nl;
            }
            catch (Exception ex)
            {                                            
                throw new Exception("Critical error: " + ex.Message + " while processing line '"+line+"'");
            }
        }



        private static int ParseArguments(string[] args)
        {

            if (!CommandLineUtil.ParseArg(args, "d", out directories))
            {
                ShowError("Missing the directories parameter (-d)");
                return 7;
            } 
            if (!CommandLineUtil.ParseArg(args, "f", out filemask))
            {
                filemask = "*.*";
            }
            return 0;
        }

        private static int ShowError(string msg)
        {
            Console.WriteLine(msg + "\r\n\r\nUsage:\r\n" +
                "-d:<list>\tComma separated list of directory paths with javascript files to be processed\r\n" +
                "-f:<mask>\tFilemask. Defaults to *.js\r\n" +
                //"-x:<list>\tComma separated list of top-leveldirectories to be excluded.\r\n" +
               "Examples:\r\n" +
                "jspp -f:c:\\code\\app,c:\\code\\util -f:*.js"
                );

            return 1;
        }
    }
}
