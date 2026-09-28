using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReTAC.Domain.Listing;

/// <summary>
/// R-112: ファイルビューの列挙を名前で読み書きし、知らない名前・数は「定義されていない値」として読む（例外にしない）。
/// 設定ファイルの全体は JsonStringEnumConverter で読むので、知らない名前は JsonException になり、AppSettings.Load が
/// 設定ファイル全体を捨てて既定値で起動してしまう。FileViewSettings.Normalize が定義されていない値を既定値に戻す。
/// 型ではなく欄に付ける（オプションの Converters は型に付けた属性より先に効くが、欄に付けた属性はそれより先に効く）。
/// </summary>
public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <summary>どの列挙でも定義していない値。Normalize が Enum.IsDefined で見分ける。</summary>
    internal static readonly T Unknown = (T)Enum.ToObject(typeof(T), int.MinValue);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named)) return named;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)) return (T)Enum.ToObject(typeof(T), number);
        reader.Skip();
        return Unknown;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>列挙の列。知らない名前の要素を捨てる（R-115 の情報の選択）。</summary>
public sealed class LenientEnumListConverter<T> : JsonConverter<IReadOnlyList<T>> where T : struct, Enum
{
    private static readonly LenientEnumConverter<T> Item = new();

    public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) { reader.Skip(); return null; }
        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var value = Item.Read(ref reader, typeof(T), options);
            if (Enum.IsDefined(value)) items.Add(value);
        }
        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item.ToString());
        writer.WriteEndArray();
    }
}
