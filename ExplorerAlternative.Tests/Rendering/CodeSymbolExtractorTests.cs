using ExplorerAlternative.Rendering;

namespace ExplorerAlternative.Tests.Rendering;

// 仕様書16章「コードシンボル表示」：言語ごとの、クラス・関数・メソッドの簡易抽出。
public sealed class CodeSymbolExtractorTests
{
    private static string[] Names(string code, string ext) =>
        CodeSymbolExtractor.Extract(code, ext).Select(s => $"{s.Kind}:{s.Name}").ToArray();

    [Theory]
    [InlineData("cs", true)]
    [InlineData(".CS", true)]
    [InlineData("py", true)]
    [InlineData("html", true)]
    [InlineData("scss", true)]
    [InlineData("txt", false)]
    [InlineData("md", false)]
    [InlineData("", false)]
    public void IsSupported_ByExtension(string ext, bool expected)
    {
        Assert.Equal(expected, CodeSymbolExtractor.IsSupported(ext));
    }

    [Fact]
    public void UnsupportedExtension_GivesNothing()
    {
        Assert.Empty(CodeSymbolExtractor.Extract("class A {}", "txt"));
    }

    [Fact]
    public void EmptyContent_GivesNothing()
    {
        Assert.Empty(CodeSymbolExtractor.Extract(string.Empty, "cs"));
    }

    // ===== C# / Java =====

    [Fact]
    public void CSharp_FindsClassesAndMethods_WithNestingDepth()
    {
        var code = "namespace N\n{\n    public class Foo\n    {\n        public void Bar(int x)\n        {\n            if (x > 0) { }\n        }\n\n        private static string Baz() => \"\";\n    }\n}\n";

        var symbols = CodeSymbolExtractor.Extract(code, "cs");

        var foo = Assert.Single(symbols, s => s.Name == "Foo");
        Assert.Equal("class", foo.Kind);
        Assert.Equal(3, foo.Line);
        var bar = Assert.Single(symbols, s => s.Name == "Bar");
        Assert.Equal("method", bar.Kind);
        Assert.Equal(5, bar.Line);
        Assert.True(bar.Depth > foo.Depth);
    }

    [Fact]
    public void CSharp_ControlFlowStatements_AreNotMethods()
    {
        var code = "class A\n{\n    void M()\n    {\n        if (a) { }\n        foreach (var x in y) { }\n        while (true) { }\n        using (var s = f()) { }\n    }\n}\n";

        Assert.DoesNotContain(Names(code, "cs"), n => n is "method:if" or "method:foreach" or "method:while" or "method:using");
    }

    [Theory]
    [InlineData("public interface IFoo", "interface:IFoo")]
    [InlineData("public struct P", "struct:P")]
    [InlineData("internal enum E", "enum:E")]
    [InlineData("public sealed record R", "record:R")]
    [InlineData("public static partial class S", "class:S")]
    public void CSharp_ContainerKinds(string line, string expected)
    {
        Assert.Contains(expected, Names(line + "\n{\n}\n", "cs"));
    }

    [Fact]
    public void Java_Works()
    {
        var code = "public class Main {\n    public static void main(String[] args) {\n    }\n}\n";

        Assert.Equal(new[] { "class:Main", "method:main" }, Names(code, "java"));
    }

    [Fact]
    public void CRLF_LineEndings_GiveTheSameResultAndLines()
    {
        var lf = CodeSymbolExtractor.Extract("class A\n{\n    void M()\n    {\n    }\n}\n", "cs");
        var crlf = CodeSymbolExtractor.Extract("class A\r\n{\r\n    void M()\r\n    {\r\n    }\r\n}\r\n", "cs");

        Assert.Equal(lf.Select(s => (s.Name, s.Line)), crlf.Select(s => (s.Name, s.Line)));
    }

    // ===== Python =====

    [Fact]
    public void Python_ClassesAndFunctions_DepthFromIndent()
    {
        var code = "class A:\n    def m(self):\n        pass\n\ndef top():\n    pass\n";

        var symbols = CodeSymbolExtractor.Extract(code, "py");

        Assert.Equal(new[] { "class:A", "function:m", "function:top" }, symbols.Select(s => $"{s.Kind}:{s.Name}"));
        Assert.Equal(new[] { 0, 1, 0 }, symbols.Select(s => s.Depth));
        Assert.Equal(new[] { 1, 2, 5 }, symbols.Select(s => s.Line));
    }

    // ===== Go =====

    [Fact]
    public void Go_FunctionsMethodsAndTypes()
    {
        var code = "package main\n\ntype Server struct {\n}\n\ntype Handler interface {\n}\n\nfunc Run() {\n}\n\nfunc (s *Server) Start() {\n}\n";

        Assert.Equal(
            new[] { "struct:Server", "interface:Handler", "function:Run", "function:Start" },
            Names(code, "go"));
    }

    // ===== JavaScript / TypeScript =====

    [Fact]
    public void JavaScript_ClassFunctionArrowAndMethod()
    {
        var code = "export class Widget {\n  render() {\n  }\n}\nfunction helper(a) {\n}\nconst add = (a, b) => a + b;\nexport async function load() {\n}\n";

        var names = Names(code, "js");

        Assert.Contains("class:Widget", names);
        Assert.Contains("method:render", names);
        Assert.Contains("function:helper", names);
        Assert.Contains("function:add", names);
        Assert.Contains("function:load", names);
    }

    [Fact]
    public void JavaScript_DoesNotRecognizeInterfaces_ButTypeScriptDoes()
    {
        var code = "export interface Options {\n  a: number;\n}\n";

        Assert.DoesNotContain("interface:Options", Names(code, "js"));
        Assert.Contains("interface:Options", Names(code, "ts"));
    }

    [Fact]
    public void JavaScript_ControlFlowInsideMethods_IsNotAMethod()
    {
        var code = "class A {\n  run() {\n    if (x) {\n    }\n    for (const a of b) {\n    }\n  }\n}\n";

        Assert.DoesNotContain(Names(code, "js"), n => n is "method:if" or "method:for");
    }

    // ===== C / C++ =====

    [Fact]
    public void C_TopLevelFunctionsAndStructs()
    {
        var code = "#include <stdio.h>\n\nstruct Point {\n    int x;\n};\n\nint main(void) {\n    return 0;\n}\n\nstatic void helper(int a)\n{\n}\n";

        var names = Names(code, "c");

        Assert.Contains("struct:Point", names);
        Assert.Contains("function:main", names);
        Assert.Contains("function:helper", names);
        Assert.DoesNotContain(names, n => n.Contains("include"));
    }

    [Fact]
    public void C_CallsInsideFunctionBodies_AreNotListed()
    {
        var code = "int main(void) {\n    printf(\"hi\");\n    foo(1);\n    return 0;\n}\n";

        Assert.Equal(new[] { "function:main" }, Names(code, "c"));
    }

    // ===== CSS / HTML =====

    [Fact]
    public void Css_Selectors_ButNotAtRules()
    {
        var code = ".a {\n  color: red;\n}\n@media (min-width: 1px) {\n}\n#id > p {\n}\n";

        Assert.Equal(new[] { "selector:.a", "selector:#id > p" }, Names(code, "css"));
    }

    [Fact]
    public void Html_HeadingsWithLevelDepth_AndIds()
    {
        var code = "<h1>Title</h1>\n<div id=\"main\"></div>\n<h2><b>Sub</b> part</h2>\n";

        var symbols = CodeSymbolExtractor.Extract(code, "html");

        var title = Assert.Single(symbols, s => s.Name == "Title");
        Assert.Equal(0, title.Depth);
        var sub = Assert.Single(symbols, s => s.Kind == "heading" && s.Name.StartsWith("Sub"));
        Assert.Equal("Sub part", sub.Name); // 見出しの中のタグは、取り除く
        Assert.Equal(1, sub.Depth);
        Assert.Contains(symbols, s => s.Kind == "id" && s.Name == "#main" && s.Line == 2);
    }

    // ===== 表示用 =====

    [Fact]
    public void DisplayText_HasAGlyphByKind()
    {
        var symbols = CodeSymbolExtractor.Extract("class A\n{\n}\n", "cs");

        Assert.EndsWith(" A", symbols[0].DisplayText);
        Assert.NotEqual("A", symbols[0].DisplayText);
    }
}
