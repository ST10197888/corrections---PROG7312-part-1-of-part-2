using System;

namespace SmartX.Core
{
    public static class NetworkHierarchyValidator
    {
        public static bool ValidateNodePlacement(DeploymentNode currentTier, string targetZoneName, int depth = 0)
        {
            if (depth > 10) 
                return false;
            
            if (currentTier == null) 
                return false;

            if (string.Equals(currentTier.Name, targetZoneName, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var childSubZone in currentTier.SubZones)
            {
                if (ValidateNodePlacement(childSubZone, targetZoneName, depth + 1))
                    return true;
            }

            return false;
        }
    }
}
