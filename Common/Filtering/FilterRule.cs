namespace Microservices.Common.Filtering;

public static class FilterCondition
{
    public const string Equal = "==";
    public const string NotEqual = "<>";
    public const string GreaterThanOrEqual = ">=";
    public const string LessThanOrEqual = "<=";
    public const string Between = "between";
    public const string Like = "like";
    public const string ILike = "ilike";
    public const string In = "in";
    public const string NotIn = "nin";
}

public class FilterRule
{
    public string Condition { get; set; } = FilterCondition.Equal;
    public string?[] Values { get; set; } = [];
}
