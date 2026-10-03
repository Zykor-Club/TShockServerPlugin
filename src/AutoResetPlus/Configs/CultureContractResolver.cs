using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AutoResetPlus.Configs;

// 序列化时按当前语言把属性名替换为 LocalizedPropertyNameAttribute 指定的文本，并应用排序
internal sealed class CultureContractResolver(string cultureName) : DefaultContractResolver
{
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);
        var attrs = member.GetCustomAttributes<LocalizedPropertyNameAttribute>().ToList();
        // 优先匹配当前语言，缺失时回退中文（与原 LazyAPI 行为一致）
        var match = attrs.FirstOrDefault(a => a.Type == cultureName)
                    ?? attrs.FirstOrDefault(a => a.Type == CultureType.Chinese);
        if (match != null)
        {
            property.PropertyName = match.Text;
            property.Order = match.Order;
        }

        return property;
    }
}