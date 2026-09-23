using System.Text;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Line-oriented, indentation-aware source builder. Emitters write lines and open blocks; they
/// never manage whitespace by hand, so output layout is uniform across emitters.
/// </summary>
internal sealed class SourceWriter
{
    private const string IndentUnit = "    ";
    private readonly StringBuilder _builder = new();
    private int _depth;

    public SourceWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            for (int i = 0; i < _depth; i++)
            {
                _builder.Append(IndentUnit);
            }

            _builder.Append(text);
        }

        _builder.Append('\n');
        return this;
    }

    /// <summary>Writes <paramref name="header"/> and <c>{</c>; disposing the scope writes <c>}</c>.</summary>
    public Scope Block(string header, string closing = "}")
    {
        Line(header);
        Line("{");
        _depth++;
        return new Scope(this, closing);
    }

    public override string ToString() => _builder.ToString();

    internal readonly struct Scope(SourceWriter writer, string closing) : System.IDisposable
    {
        public void Dispose()
        {
            writer._depth--;
            writer.Line(closing);
        }
    }
}
