using System.Collections.Generic;

namespace SmartX.Core
{
    public class DeploymentNode
    {
        public string Name { get; set; }
        public List<DeploymentNode> SubZones { get; set; }

        public DeploymentNode(string name)
        {
            Name = name;
            SubZones = new List<DeploymentNode>();
        }
    }
}
