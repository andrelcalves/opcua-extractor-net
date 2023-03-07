﻿/* Cognite Extractor for OPC-UA
Copyright (C) 2023 Cognite AS

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
using System.ComponentModel;

namespace Cognite.OpcUa.Config
{
    public class DataSubscriptionConfig
    {
        /// <summary>
        /// What changes to a variable trigger an update.
        /// One of Status, StatusValue, or StatusValueTimestamp.
        /// </summary>
        [DefaultValue(DataChangeTrigger.StatusValue)]
        public DataChangeTrigger Trigger { get => filter.Trigger; set => filter.Trigger = value; }
        /// <summary>
        /// Deadband for numeric values.
        /// One of None, Absolute, or Percent.
        /// </summary>
        [DefaultValue(DeadbandType.None)]
        public DeadbandType DeadbandType { get => (DeadbandType)filter.DeadbandType; set => filter.DeadbandType = (uint)value; }
        /// <summary>
        /// Value of deadband.
        /// </summary>
        public double DeadbandValue { get => filter.DeadbandValue; set => filter.DeadbandValue = value; }
        private readonly DataChangeFilter filter = new DataChangeFilter()
        {
            Trigger = DataChangeTrigger.StatusValue,
            DeadbandType = (uint)DeadbandType.None,
            DeadbandValue = 0.0
        };
        public DataChangeFilter Filter => filter;
    }
    public class SubscriptionConfig
    {
        /// <summary>
        /// Modify the DataChangeFilter used for datapoint subscriptions. See OPC-UA reference part 4 7.17.2 for details.
        /// These are just passed to the server, they have no effect on extractor behavior.
        /// Filters are applied to all nodes, but deadband should only affect some, according to the standard.
        /// </summary>
        public DataSubscriptionConfig? DataChangeFilter { get; set; }
        /// <summary>
        /// Enable subscriptions on data-points.
        /// </summary>
        public bool DataPoints { get; set; } = true;
        /// <summary>
        /// Enable subscriptions on events. Requires events.enabled to be set to true.
        /// </summary>
        public bool Events { get; set; } = true;
        /// <summary>
        /// Ignore the access level parameter for history and datapoints.
        /// This means using the "Historizing" parameter for history, and subscribing to all timeseries, independent of AccessLevel.
        /// </summary>
        public bool IgnoreAccessLevel { get; set; }
        /// <summary>
        /// Log bad subscription datapoints
        /// </summary>
        public bool LogBadValues { get; set; } = true;
    }
}