using System.Collections.Generic;

namespace CivicSurvival.Core.Types
{
    public static class BuildingCategoryCollectionExtensions
    {
        public static bool Contains(this IReadOnlyCollection<BuildingCategory> categories, BuildingCategory category)
        {
            if (categories == null)
                return false;

            foreach (var item in categories)
            {
                if (item == category)
                    return true;
            }

            return false;
        }
    }
}
