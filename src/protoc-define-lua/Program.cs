using System.Diagnostics;
using System.Text.RegularExpressions;

var currentDirectory = Environment.CurrentDirectory;
var fileSystemInfo = new DirectoryInfo(args.Length != 0 ? args[0].Replace(".", currentDirectory) : currentDirectory);
var directoryInfo = new DirectoryInfo(args.Length > 1 ? args[1].Replace(".", currentDirectory) : currentDirectory + "/out/lua");
//var directoryInfo2 = new DirectoryInfo((args.Length > 2) ? args[2].Replace(".", currentDirectory) : (currentDirectory + "/out/go"));
var files = Directory.GetFiles(fileSystemInfo.FullName, "*.proto", SearchOption.AllDirectories);
var dictionary = new Dictionary<string, string>();
var text2 = "--================================AUTO GENERATE FILE DO NOT EDIT================================";
Console.WriteLine(string.Concat("Lua:", directoryInfo));

foreach (var t in files) {
    var process = new Process();
    process.StartInfo.FileName = $"{Environment.CurrentDirectory}\\clang-format.exe";
    var style = "\"{BasedOnStyle: Google, ColumnLimit: 0, IndentWidth: 2, PenaltyBreakComment : 0}\"";
    process.StartInfo.Arguments = $"-i {t} -style={style}";
    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
    process.Start();
    process.WaitForExit();
    foreach (string text in File.ReadAllLines(t)) {
        if (text.IndexOf("//@", StringComparison.Ordinal) != -1) {
            string[] array3 = text.Split('=');
            var key = array3[0].TrimStart("//@".ToCharArray()).Trim();
            if (!dictionary.ContainsKey(key))
                dictionary.Add(key, array3[1].Trim());
            else {
                Console.WriteLine($"生成失败!有相同的键值存在： {key}");
                return;
            }
        }
    }

    var regexLine = new Regex(@".+$", RegexOptions.Multiline);
    var regexCommentBefore = new Regex(@"(?<before>.*)//");
    var regexComment = new Regex(@"\s*//\s*(?<comment>.+)");
    var regexMap = new Regex(@"map<(?<type1>.+),\s*(?<type2>.+)>\s*(?<name>.+)=\s*(?<index>\d+)\s*;");
    var regexField = new Regex(@"(?<type>.+)\s(?<name>.+)=\s(?<index>\d+)\s*;");
    var regexMessage = new Regex(@"(?<comment>/*[.\s\S]*?)message(?<class>[.\s\S]*?){(?<content>[.\s\S]*?)}", RegexOptions.Multiline);
    foreach (Match matchFile in regexMessage.Matches(File.ReadAllText(t))) {
        var comment = matchFile.Groups["comment"].Value;
        var classComment = "";
        foreach (Match matchComment in regexLine.Matches(comment)) {
            if (!matchComment.Value.Contains("//") || matchComment.Value.Contains("//@")) continue;
            classComment += $"{(classComment.Length > 0 ? " " : "")}{matchComment.Value.Replace("//", "").Trim()}";
        }
        var className = matchFile.Groups["class"].Value.Trim();
        var content = matchFile.Groups["content"].Value;
        text2 += $"\n\n---@class pb.{className} {classComment}";
        string upComment = "";
        foreach (Match matchContent in regexLine.Matches(content)) {
            var line = matchContent.Value;
            var beforeComment = regexCommentBefore.Match(line);
            if (line == "\r") continue;
            var label = string.Empty;
            if (line.Contains("repeated")) label = "repeated";
            else if (line.Contains("optional")) label = "optional";
            if (!string.IsNullOrEmpty(label)) line = line.Replace(label, "");
            Match commentMatch = regexComment.Match(line);
            upComment += $"{(upComment.Length > 0 ? " " : "")}{commentMatch.Groups["comment"].Value.Trim()}";
            if (beforeComment.Success && beforeComment.Groups["before"].Value.Trim().Length == 0) continue;
            
            if (line.Contains("map<")) {
                Match matchMap = regexMap.Match(line);
                if (matchMap.Success) {
                    var type1 = matchMap.Groups["type1"].Value.Trim();
                    var type2 = matchMap.Groups["type2"].Value.Trim();
                    var name = matchMap.Groups["name"].Value.Trim();
                    text2 += $"\n---@field {name} table<{ConvertToLuatype(type1)}, {ConvertToLuatype(type2)}> {upComment}";
                    upComment = "";
                }
            }
            else {
                Match matchField = regexField.Match(line);
                if (matchField.Success) {
                    var type = matchField.Groups["type"].Value.Trim();
                    var name = matchField.Groups["name"].Value.Trim();
                    text2 +=
                        $"\n---@field {name} {ConvertToLuatype(type)}{(label == "repeated" ? "[]" : "")} {(label == "optional" ? "optional*" : "")}{upComment}";
                    upComment = "";
                }
            }
        }
    }
}

Console.WriteLine($"成功导出{dictionary.Count}个键值");
if (dictionary.Count == 0) {
    return;
}

var dictionary2 = (from p in dictionary orderby p.Key select p).ToDictionary(p => p.Key, o => o.Value);
text2 += "\n\nProtoID = {\n";
foreach (KeyValuePair<string, string> keyValuePair in dictionary2) {
    text2 += $"    {keyValuePair.Key.Replace('.', '_')} = \"{keyValuePair.Value}\",\n";
}

text2 += "}\n\n";

text2 += "RouteID = {\n";
foreach (KeyValuePair<string, string> keyValuePair in dictionary2) {
    if (keyValuePair.Key.Contains("_sc")) continue;
    text2 +=
        $"    {keyValuePair.Key.Replace('.', '_')} = \"{keyValuePair.Key.Replace("_cs", "")}\",\n";
}

text2 += "}";

if (!directoryInfo.Exists) {
    directoryInfo.Create();
}

File.WriteAllText($"{directoryInfo}/protoid.lua.txt", text2);


string ConvertToLuatype(string input) {
    return input switch {
        "bool" => "boolean",
        "string" or "bytes" => "string",
        "double" or "float" or "int32" or "uint32" or "fixed32" or " sfixed32" or " sint32" or "int64" or "uint64" or "fixed64" or "sfixed64"
            or "sint64" => "number",
        _ => $"pb.{input}"
    };
}