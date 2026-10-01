using System.Reflection;

namespace ExplorerAlternative.Tests.TestDoubles;

/// <summary>
/// モック用のライブラリを使わずに、任意のインターフェースの偽物を作るための汎用スタブ。
/// 既定では、どのメソッドも「何もしない・既定値を返す」（コレクションは空、Taskは完了済み、
/// 参照型はnull、値型は既定値）。テストで確認したいメソッドだけ、<see cref="On"/>で振る舞いを
/// 差し替え、<see cref="Calls"/>で呼び出しを確認する。
/// </summary>
public class StubProxy : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = new();

    /// <summary>呼び出されたメソッド名と引数（呼び出された順）。</summary>
    public List<(string Method, object?[] Args)> Calls { get; } = new();

    public static T Create<T>() where T : class => DispatchProxy.Create<T, StubProxy>();

    /// <summary>作成したスタブの制御用オブジェクトを取り出す。</summary>
    public static StubProxy Of(object stub) => (StubProxy)stub;

    /// <summary>指定したメソッド名の振る舞いを差し替える（引数を受け取り、戻り値を返す）。</summary>
    public StubProxy On(string method, Func<object?[], object?> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    public IEnumerable<object?[]> ArgsOf(string method) => Calls.Where(c => c.Method == method).Select(c => c.Args);

    public int CountOf(string method) => Calls.Count(c => c.Method == method);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        var arguments = args ?? Array.Empty<object?>();
        Calls.Add((name, arguments));

        if (_handlers.TryGetValue(name, out var handler))
        {
            return handler(arguments);
        }

        return DefaultValue(targetMethod.ReturnType);
    }

    private static object? DefaultValue(Type type)
    {
        if (type == typeof(void))
        {
            return null;
        }

        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            var result = DefaultValue(inner);
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { result });
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        // IReadOnlyList<T> / IEnumerable<T> / IReadOnlyDictionary など：空のコレクションを返す。
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyCollection<>) || definition == typeof(IList<>))
            {
                return Array.CreateInstance(arguments[0], 0);
            }

            if (definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(IDictionary<,>))
            {
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
            }
        }

        return null;
    }
}
