using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Microservices.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Microservices.Common.Filtering;

public static class QueryableFilterExtensions
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropertyCache = new();

    public static IQueryable<T> ApplyFilters<T>(
        this IQueryable<T> query,
        IDictionary<string, FilterRule>? filters)
    {
        if (filters is null || filters.Count == 0)
            return query;

        var entityType = typeof(T);
        var properties = GetProperties(entityType);
        var parameter = Expression.Parameter(entityType, "x");
        Expression? combinedExpression = null;

        foreach (var (key, rule) in filters)
        {
            if (rule.Values == null || rule.Values.Length == 0)
                continue;

            if (!properties.TryGetValue(key, out var propertyInfo))
                continue;

            var filterExpr = BuildFilterExpression(parameter, propertyInfo, rule);
            if (filterExpr is null)
                continue;

            combinedExpression = combinedExpression is null
                ? filterExpr
                : Expression.AndAlso(combinedExpression, filterExpr);
        }

        if (combinedExpression is null)
            return query;

        var lambda = Expression.Lambda<Func<T, bool>>(combinedExpression, parameter);
        return query.Where(lambda);
    }

    public static IQueryable<T> ApplySorting<T>(
        this IQueryable<T> query,
        string? sortBy,
        bool sortDescending = true)
    {
        var entityType = typeof(T);
        var properties = GetProperties(entityType);

        PropertyInfo? propertyInfo = null;
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            properties.TryGetValue(sortBy, out propertyInfo);
        }

        if (propertyInfo is null)
        {
            if (!properties.TryGetValue("Id", out propertyInfo) &&
                !properties.TryGetValue("CreatedAt", out propertyInfo))
            {
                propertyInfo = properties.Values.FirstOrDefault();
            }
        }

        if (propertyInfo is null)
            return query;

        var parameter = Expression.Parameter(entityType, "x");
        var propertyAccess = Expression.MakeMemberAccess(parameter, propertyInfo);
        var orderByExpression = Expression.Lambda(propertyAccess, parameter);

        var methodName = sortDescending ? "OrderByDescending" : "OrderBy";
        var resultExpression = Expression.Call(
            typeof(Queryable),
            methodName,
            [entityType, propertyInfo.PropertyType],
            query.Expression,
            Expression.Quote(orderByExpression));

        return query.Provider.CreateQuery<T>(resultExpression);
    }

    public static IQueryable<T> ApplyPaging<T>(
        this IQueryable<T> query,
        int page,
        int size)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 20;

        return query.Skip((page - 1) * size).Take(size);
    }

    public static IQueryable<T> ApplyPaging<T>(
        this IQueryable<T> query,
        int? page,
        int? size) =>
        query.ApplyPaging(page ?? 1, size ?? 20);

    public static IQueryable<T> ApplyPaging<T>(
        this IQueryable<T> query,
        IPagedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return query.ApplyPaging(request.Page, request.PageSize);
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedRequest request,
        IDictionary<string, FilterRule>? filters = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var baseQuery = query.AsNoTracking();

        if (filters is not null && filters.Count > 0)
        {
            baseQuery = baseQuery.ApplyFilters(filters);
        }

        var total = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .ApplySorting(request.SortBy, request.SortDescending)
            .ApplyPaging(request.Page, request.Size)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, total);
    }

    private static Expression? BuildFilterExpression(
        ParameterExpression parameter,
        PropertyInfo propertyInfo,
        FilterRule rule)
    {
        var memberAccess = Expression.MakeMemberAccess(parameter, propertyInfo);
        var targetType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;

        var rawValue = rule.Values.FirstOrDefault(v => v != null);
        var convertedValue = ConvertValue(rawValue, targetType);

        Expression targetValueExpr = Expression.Constant(convertedValue, targetType);
        Expression left = memberAccess;

        if (propertyInfo.PropertyType != targetType)
        {
            targetValueExpr = Expression.Convert(targetValueExpr, propertyInfo.PropertyType);
        }

        switch (rule.Condition.ToLowerInvariant())
        {
            case FilterCondition.Equal:
                return Expression.Equal(left, targetValueExpr);

            case FilterCondition.NotEqual:
                return Expression.NotEqual(left, targetValueExpr);

            case FilterCondition.GreaterThanOrEqual:
                return Expression.GreaterThanOrEqual(left, targetValueExpr);

            case FilterCondition.LessThanOrEqual:
                return Expression.LessThanOrEqual(left, targetValueExpr);

            case FilterCondition.Between:
                if (rule.Values.Length < 2)
                    return null;

                var fromVal = ConvertValue(rule.Values[0], targetType);
                var toVal = ConvertValue(rule.Values[1], targetType);

                if (fromVal is null || toVal is null)
                    return null;

                Expression fromExpr = Expression.Constant(fromVal, targetType);
                Expression toExpr = Expression.Constant(toVal, targetType);

                if (propertyInfo.PropertyType != targetType)
                {
                    fromExpr = Expression.Convert(fromExpr, propertyInfo.PropertyType);
                    toExpr = Expression.Convert(toExpr, propertyInfo.PropertyType);
                }

                var gte = Expression.GreaterThanOrEqual(left, fromExpr);
                var lte = Expression.LessThanOrEqual(left, toExpr);
                return Expression.AndAlso(gte, lte);

            case FilterCondition.Like:
            case FilterCondition.ILike:
                if (propertyInfo.PropertyType != typeof(string) || rawValue is null)
                    return null;

                var stringContainsMethod = typeof(string).GetMethod(
                    nameof(string.Contains),
                    [typeof(string), typeof(StringComparison)])!;

                var comparison = rule.Condition.Equals(FilterCondition.ILike, StringComparison.OrdinalIgnoreCase)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                var searchConst = Expression.Constant(rawValue, typeof(string));
                var comparisonConst = Expression.Constant(comparison, typeof(StringComparison));

                var notNullCheck = Expression.NotEqual(left, Expression.Constant(null, typeof(string)));
                var containsCall = Expression.Call(left, stringContainsMethod, searchConst, comparisonConst);
                return Expression.AndAlso(notNullCheck, containsCall);

            case FilterCondition.In:
            case FilterCondition.NotIn:
                var validValues = rule.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => ConvertValue(v, targetType))
                    .Where(v => v != null)
                    .ToList();

                if (validValues.Count == 0)
                    return null;

                var listType = typeof(List<>).MakeGenericType(targetType);
                var listInstance = Activator.CreateInstance(listType)!;
                var addMethod = listType.GetMethod(nameof(List<object>.Add))!;

                foreach (var item in validValues)
                {
                    addMethod.Invoke(listInstance, [item]);
                }

                var containsMethod = listType.GetMethod(nameof(List<object>.Contains), [targetType])!;
                var listConstant = Expression.Constant(listInstance, listType);

                Expression inExpr = Expression.Call(listConstant, containsMethod, memberAccess);

                return rule.Condition.Equals(FilterCondition.NotIn, StringComparison.OrdinalIgnoreCase)
                    ? Expression.Not(inExpr)
                    : inExpr;

            default:
                return null;
        }
    }

    private static object? ConvertValue(string? value, Type targetType)
    {
        if (value is null)
            return null;

        if (targetType == typeof(string))
            return value;

        if (targetType.IsEnum)
        {
            return Enum.TryParse(targetType, value, true, out var enumResult)
                ? enumResult
                : null;
        }

        if (targetType == typeof(Guid))
            return Guid.TryParse(value, out var g) ? g : null;

        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(value, out var dto) ? dto : null;

        if (targetType == typeof(DateTime))
            return DateTime.TryParse(value, out var dt) ? dt : null;

        try
        {
            var converter = TypeDescriptor.GetConverter(targetType);
            return converter.CanConvertFrom(typeof(string))
                ? converter.ConvertFromInvariantString(value)
                : Convert.ChangeType(value, targetType);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, PropertyInfo> GetProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             .Where(p => p.CanRead)
             .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase));
    }
}
