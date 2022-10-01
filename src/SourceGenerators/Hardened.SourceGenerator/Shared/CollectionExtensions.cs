namespace Hardened.SourceGenerator.Shared;

public static class CollectionExtensions
{
    public static bool DeepEquals<T>(this IReadOnlyList<T> x, IReadOnlyList<T> y)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        for (var i = 0; i < x.Count; i++)
        {
            var xValue = x[i];
            var yValue = y[i];

            if (xValue != null)
            {
                if (!xValue.Equals(yValue))
                {
                    return false;
                }
            }
            else if (yValue != null)
            {
                return false;
            }
        }

        return true;
    }

    public static int GetHashCodeAggregation<T>(this IReadOnlyCollection<T> collection)
    {
        int hashCode = 123;

        foreach (var value in collection)
        {
            hashCode = (hashCode * 397) ^ value!.GetHashCode();
        }

        return hashCode;
    }
}