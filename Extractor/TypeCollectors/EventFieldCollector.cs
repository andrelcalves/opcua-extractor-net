﻿/* Cognite Extractor for OPC-UA
Copyright (C) 2020 Cognite AS

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA. */

using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Cognite.OpcUa.TypeCollectors
{
    public class EventField
    {
        public QualifiedNameCollection BrowsePath { get; }
        public string Name => string.Join('_', BrowsePath.Select(name => name.Name));
        public EventField(QualifiedName browseName)
        {
            BrowsePath = new QualifiedNameCollection() { browseName };
        }
        public EventField(QualifiedNameCollection browsePath)
        {
            BrowsePath = browsePath;
        }
        // The default hash code of browsename does not include the namespaceindex for some reason.
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 0;
                foreach (var name in BrowsePath)
                {
                    hash *= 31;
                    hash += HashCode.Combine(name.Name, name.NamespaceIndex);
                }
                return hash;
            }
        }
        public override bool Equals(object other)
        {
            if (!(other is EventField otherField)) return false;
            if (BrowsePath.Count != otherField.BrowsePath.Count) return false;

            for (int i = 0; i < BrowsePath.Count; i++)
            {
                if (BrowsePath[i].Name != otherField.BrowsePath[i].Name
                    || BrowsePath[i].NamespaceIndex != otherField.BrowsePath[i].NamespaceIndex) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Collects the fields of events. It does this by mapping out the entire event type hierarchy,
    /// and collecting the fields of each node on the way.
    /// </summary>
    public class EventFieldCollector
    {
        private readonly UAClient uaClient;
        private readonly Dictionary<NodeId, UAEventType> types = new Dictionary<NodeId, UAEventType>();
        private readonly Dictionary<NodeId, ChildNode> nodes = new Dictionary<NodeId, ChildNode>();
        private readonly EventConfig config;
        private readonly Regex ignoreFilter;
        private HashSet<string> excludeProperties;
        private HashSet<string> baseExcludeProperties;
        /// <summary>
        /// Construct the collector.
        /// </summary>
        /// <param name="parent">UAClient to be used for browse calls.</param>
        /// <param name="targetEventIds">Target event ids</param>
        public EventFieldCollector(UAClient parent, EventConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            uaClient = parent;
            this.config = config;
            if (!string.IsNullOrEmpty(config.ExcludeEventFilter))
            {
                ignoreFilter = new Regex(config.ExcludeEventFilter, RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            }
            excludeProperties = new HashSet<string>(config.ExcludeProperties);
            baseExcludeProperties = new HashSet<string>(config.BaseExcludeProperties);
        }
        /// <summary>
        /// Main collection function. Calls BrowseDirectory on BaseEventType, waits for it to complete, which should populate properties and localProperties,
        /// then collects the resulting fields in a dictionary on the form EventTypeId -> (SourceTypeId, BrowseName).
        /// </summary>
        /// <returns>The collected fields in a dictionary on the form EventTypeId -> (SourceTypeId, BrowseName).</returns>
        public Dictionary<NodeId, HashSet<EventField>> GetEventIdFields(CancellationToken token)
        {
            types[ObjectTypeIds.BaseEventType] = new UAEventType
            {
                Id = ObjectTypeIds.BaseEventType,
                DisplayName = "BaseEventType"
            };

            uaClient.BrowseDirectory(
                new List<NodeId> { ObjectTypeIds.BaseEventType },
                EventTypeCallback,
                token,
                ReferenceTypeIds.HierarchicalReferences,
                (uint)NodeClass.ObjectType | (uint)NodeClass.Variable | (uint)NodeClass.Object,
                false,
                false,
                true);

            var result = new Dictionary<NodeId, HashSet<EventField>>();

            HashSet<NodeId> whitelist = null;
            if (config.EventIds != null && config.EventIds.Any())
            {
                whitelist = new HashSet<NodeId>(config.EventIds.Select(proto => proto.ToNodeId(uaClient, ObjectTypeIds.BaseEventType)));
            }

            foreach (var type in types.Values)
            {
                if (ignoreFilter != null && ignoreFilter.IsMatch(type.DisplayName.Text)) continue;
                if (whitelist != null && whitelist.Any())
                {
                    if (!whitelist.Contains(type.Id)) continue;
                }
                else if (!config.AllEvents && type.Id.NamespaceIndex == 0) continue;
                result[type.Id] = new HashSet<EventField>(type.CollectedFields);
            }

            return result;
        }
        /// <summary>
        /// HandleNode callback for the event type mapping.
        /// </summary>
        /// <param name="child">Type or property to be handled</param>
        /// <param name="parent">Parent type id</param>
        private void EventTypeCallback(ReferenceDescription child, NodeId parent)
        {
            var id = uaClient.ToNodeId(child.NodeId);

            if (child.NodeClass == NodeClass.ObjectType)
            {
                var parentType = types.GetValueOrDefault(parent);
                types[id] = new UAEventType
                {
                    Id = id,
                    Parent = parentType,
                    DisplayName = child.DisplayName
                };
            }
            else if (child.NodeClass == NodeClass.Object
                || child.NodeClass == NodeClass.Variable)
            {
                ChildNode node;
                if (types.TryGetValue(parent, out var parentType))
                {
                    if (parent == ObjectTypeIds.BaseEventType && baseExcludeProperties.Contains(child.BrowseName.Name)
                        || excludeProperties.Contains(child.BrowseName.Name)) return;
                    node = parentType.AddChild(child);
                }
                else if (nodes.TryGetValue(parent, out var parentNode))
                {
                    node = parentNode.AddChild(child);
                }
                else
                {
                    return;
                }
                if (child.NodeClass != NodeClass.Variable
                    || child.TypeDefinition != VariableTypeIds.PropertyType)
                {
                    nodes[id] = node;
                }
            }
        }
        private class UAEventType
        {
            public NodeId Id { get; set; }
            public LocalizedText DisplayName { get; set; }
            public UAEventType Parent { get; set; }
            private IList<ChildNode> children = new List<ChildNode>();
            public ChildNode AddChild(ReferenceDescription desc)
            {
                var node = new ChildNode(desc.BrowseName, desc.NodeClass);
                children.Add(node);
                return node;
            }
            public IEnumerable<EventField> CollectedFields { get
            {
                var childFields = children.SelectMany(child => child.ToFields());
                return Parent?.CollectedFields?.Concat(childFields) ?? childFields;
            } }
        }
        private class ChildNode
        {
            private NodeClass nodeClass;
            private QualifiedName browseName;
            private IList<ChildNode> children;

            public ChildNode(QualifiedName browseName, NodeClass nc)
            {
                this.browseName = browseName;
                nodeClass = nc;
            }
            public ChildNode AddChild(ReferenceDescription desc)
            {
                var node = new ChildNode(desc.BrowseName, desc.NodeClass);
                if (children == null)
                {
                    children = new List<ChildNode> { node };
                }
                children.Add(node);
                return node;
            }
            public IEnumerable<EventField> ToFields()
            {
                if (nodeClass == NodeClass.Object && children == null) yield break;
                if (nodeClass == NodeClass.Variable)
                {
                    yield return new EventField(browseName);
                }
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        var childFields = child.ToFields();
                        foreach (var childField in childFields)
                        {
                            childField.BrowsePath.Insert(0, browseName);
                            yield return childField;
                        }
                    }
                }
            }
        }
    }
}