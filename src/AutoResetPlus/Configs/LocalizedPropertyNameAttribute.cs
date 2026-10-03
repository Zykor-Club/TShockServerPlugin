namespace AutoResetPlus.Configs;

// 标记属性的本地化名称与序列化顺序（对标 LazyAPI 的同名特性，去掉外部依赖）
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class LocalizedPropertyNameAttribute(string type, string text) : Attribute
{
    public string Type { get; } = type;

    public string Text { get; } = text;

    // 同一层级内的序列化顺序
    public int Order { get; set; }
}