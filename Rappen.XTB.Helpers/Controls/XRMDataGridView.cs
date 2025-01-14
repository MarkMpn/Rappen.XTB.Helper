﻿using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Rappen.XRM.Helpers.Extensions;
using Rappen.XRM.Helpers.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace Rappen.XTB.Helpers.Controls
{
    public partial class XRMDataGridView : DataGridView
    {
        #region Private properties

        private IOrganizationService organizationService;
        private IEnumerable<Entity> entities;
        private bool autoRefresh = true;
        private bool showFriendlyNames = false;
        private bool showIdColumn = true;
        private bool showIndexColumn = true;
        private bool showLocalTimes = false;
        private bool designedColumnsDetermined = false;
        private bool designedColumnsUsed = false;
        private List<string> filterColumns = null;
        private string filterText = null;
        private string[] columnOrder = new string[] { };
        private bool showAllColumnsInColumnOrder = false;
        private bool showColumnsNotInColumnOrder = true;
        private DataGridViewColumn[] designedColumns;
        private string layoutxml;
        private Dictionary<string, int> cellsFromLayoutXML;

        private const string _extendedMetaAttribute = "MetaAttribute";
        private const string _extendedMetaEntity = "MetaEntity";

        #endregion Private properties

        #region Constructor

        public XRMDataGridView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            InitializeComponent();
            base.ReadOnly = true;
            base.AllowUserToAddRows = false;
            base.AllowUserToDeleteRows = false;
            AllowUserToOrderColumns = true;
            AllowUserToResizeRows = false;
            CellClick += HandleClick;
            CellDoubleClick += HandleDoubleClick;
            CellEnter += HandleCellEnter;
            CellLeave += HandleCellLeave;
            CellMouseEnter += HandleCellMouseEnter;
            CellMouseLeave += HandleCellMouseLeave;
        }

        #endregion Constructor

        #region Published properties

        [Category("Rappen XRM")]
        [Browsable(false)]
        public new bool ReadOnly { get; set; } = true;

        [Category("Rappen XRM")]
        [Browsable(false)]
        public new bool AllowUserToAddRows { get; set; } = false;

        [Category("Rappen XRM")]
        [Browsable(false)]
        public new bool AllowUserToDeleteRows { get; set; } = false;

        [Category("Rappen XRM")]
        [Description("Indicates the source of data (IEnumerable<Entity> or EntityCollection) for the CRMGridView control.")]
        [Browsable(false)]
        public new object DataSource
        {
            get
            {
                if (entities != null)
                {
                    return entities;
                }
                return base.DataSource;
            }
            set
            {
                designedColumns = new DataGridViewColumn[Columns.Count];
                Columns.CopyTo(designedColumns, 0);

                if (value is EntityCollection entityCollection)
                {
                    entities = entityCollection.Entities;
                    EntityName = entityCollection.EntityName;
                }
                else if (value is IEnumerable<Entity> entitylist)
                {
                    entities = entitylist;
                    EntityName = entities?.FirstOrDefault(e => !string.IsNullOrEmpty(e.LogicalName))?.LogicalName ?? string.Empty;
                }
                else
                {
                    entities = null;
                    EntityName = string.Empty;
                    base.DataSource = value;
                }
                if (entities?.Where(e => !string.IsNullOrEmpty(e.LogicalName)).Select(e => e.LogicalName).Distinct().Count() > 1)
                {
                    throw new ArgumentException("DataSource can only contain entities of the same type.");
                }

                if (designedColumnsDetermined && designedColumnsUsed && designedColumns != null)
                {
                    foreach (DataGridViewColumn col in designedColumns)
                    {
                        if (!Columns.Contains(col.Name))
                        {
                            col.DisplayIndex = Columns.Count;
                            Columns.Add(col);
                        }
                    }
                }
                if (entities != null && autoRefresh)
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [Browsable(false)]
        public string LayoutXML
        {
            get { return layoutxml; }
            set
            {
                layoutxml = value;
                cellsFromLayoutXML = GetCellsFromLayoutXML(layoutxml);
                if (entities != null && autoRefresh)
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(true)]
        [Description("Specify if content shall be automatically refreshed when datasource, service, flags etc are changed.")]
        public bool AutoRefresh
        {
            get { return autoRefresh; }
            set
            {
                if (value != autoRefresh)
                {
                    autoRefresh = value;
                    if (autoRefresh)
                    {
                        Refresh();
                    }
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(null)]
        [Description("Columns to investigate when filtering rows with FilterText. Enter datacolumn name separated by comma.")]
        public string FilterColumns
        {
            get => filterColumns == null ? string.Empty : string.Join(", ", filterColumns);
            set
            {
                filterColumns = string.IsNullOrWhiteSpace(value) ? null :
                    value.ToLowerInvariant().Split(',').Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
                if (autoRefresh)
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(null)]
        [Description("Text to search for in FilterColumns to filter visible rows in the grid.")]
        public string FilterText
        {
            get => filterText;
            set
            {
                filterText = value.ToLowerInvariant();
                if (autoRefresh)
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(null)]
        [Description("Comma separated list of column names to arrange column order by. Valid only for generic column layout.")]
        public string ColumnOrder
        {
            get => columnOrder == null ? string.Empty : string.Join(", ", columnOrder);
            set
            {
                var lastvalue = columnOrder != null ? string.Join(",", columnOrder) : string.Empty;
                columnOrder = value?.Split(',', '\n').Select(c => c.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray() ?? new string[] { };
                showAllColumnsInColumnOrder = showAllColumnsInColumnOrder && columnOrder?.Length > 0;
                showColumnsNotInColumnOrder = showColumnsNotInColumnOrder || columnOrder?.Length == 0;
                if (autoRefresh && !lastvalue.Equals(string.Join(",", columnOrder)))
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(false)]
        [Description("True to show all columns that are listed in ColumnOrder, false to only show populated columns.")]
        public bool ShowAllColumnsInColumnOrder
        {
            get => showAllColumnsInColumnOrder;
            set
            {
                var lastvalue = showAllColumnsInColumnOrder;
                showAllColumnsInColumnOrder = value && columnOrder?.Length > 0;
                if (autoRefresh && !lastvalue.Equals(showAllColumnsInColumnOrder))
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(true)]
        [Description("True to show columns that are in the data but not listed in ColumnOrder, false to ignore them.")]
        public bool ShowColumnsNotInColumnOrder
        {
            get => showColumnsNotInColumnOrder;
            set
            {
                var lastvalue = showColumnsNotInColumnOrder;
                showColumnsNotInColumnOrder = value || columnOrder?.Length == 0;
                if (autoRefresh && !lastvalue.Equals(showColumnsNotInColumnOrder))
                {
                    Refresh();
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(false)]
        [Description("True to show timestamps in local time, false to show UTC. Only valid when ShowFriendlyNames is true.")]
        public bool ShowLocalTimes
        {
            get { return showLocalTimes; }
            set
            {
                if (value != showLocalTimes)
                {
                    showLocalTimes = value;
                    if (autoRefresh)
                    {
                        Refresh();
                    }
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(false)]
        [Description("True to show friendly names, False to show logical names and guid etc.")]
        public bool ShowFriendlyNames
        {
            get { return showFriendlyNames; }
            set
            {
                if (value != showFriendlyNames)
                {
                    showFriendlyNames = value;
                    if (autoRefresh)
                    {
                        Refresh();
                    }
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(true)]
        [Description("Set this to show the id of each record first in the grid.")]
        public bool ShowIdColumn
        {
            get { return showIdColumn; }
            set
            {
                if (value != ShowIdColumn)
                {
                    showIdColumn = value;
                    if (autoRefresh)
                    {
                        Refresh();
                    }
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(true)]
        [Description("Set this to display a counter column first in the grid.")]
        public bool ShowIndexColumn
        {
            get { return showIndexColumn; }
            set
            {
                if (value != showIndexColumn)
                {
                    showIndexColumn = value;
                    if (autoRefresh)
                    {
                        Refresh();
                    }
                }
            }
        }

        [Category("Rappen XRM")]
        [DefaultValue(false)]
        [Description("Set this to give EntityReference cells a clickable appearance.")]
        public bool EntityReferenceClickable { get; set; } = false;

        #endregion Published properties

        #region Published events

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordClick;

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordDoubleClick;

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordEnter;

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordLeave;

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordMouseEnter;

        [Category("Rappen XRM")]
        public event XRMRecordEventHandler RecordMouseLeave;

        #endregion Published events

        #region Public properties

        [Browsable(false)]
        public IOrganizationService Service
        {
            get { return organizationService; }
            set
            {
                organizationService = value;
                if (autoRefresh)
                {
                    Refresh();
                }
            }
        }

        [Browsable(false)]
        public string EntityName { get; private set; } = string.Empty;

        /// <summary>
        /// IEnumerable<Entity> representing currently selected rows
        /// </summary>
        [Browsable(false)]
        public IEnumerable<Entity> SelectedRowRecords
        {
            get
            {
                if (entities == null)
                {
                    return null;
                }
                var result = new List<Entity>();
                foreach (DataGridViewRow row in SelectedRows.OfType<DataGridViewRow>().OrderBy(r => r.Index))
                {
                    var entity = row.Cells["#entity"].Value as Entity;
                    if (entity != null)
                    {
                        result.Add(entity);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// IEnumerable<Entity> representing all currently selected cells
        /// </summary>
        [Browsable(false)]
        public IEnumerable<Entity> SelectedCellRecords
        {
            get
            {
                if (entities == null)
                {
                    return null;
                }
                var result = new List<Entity>();
                foreach (DataGridViewCell cell in SelectedCells)
                {
                    if (cell.RowIndex >= 0 && cell.RowIndex < Rows.Count)
                    {
                        var row = Rows[cell.RowIndex];
                        var entity = row.Cells["#entity"].Value as Entity;
                        if (entity != null && !result.Contains(entity))
                        {
                            result.Add(entity);
                        }
                    }
                }
                return result;
            }
        }

        #endregion Public properties

        #region Public methods

        /// <summary>
        /// Gets the DataSource object as requested type.
        /// For the CRMGridView the primary expected types T are IEnumerable&lt;Entity&gt; or EntityCollection or DataTable.
        /// </summary>
        /// <typeparam name="T">Type of the DataSource to return.</typeparam>
        /// <returns>DataSource of type T if available, otherwise null.</returns>
        public T GetDataSource<T>()
        {
            if (typeof(T) == typeof(EntityCollection))
            {
                var ec = new EntityCollection { EntityName = EntityName };
                ec.Entities.AddRange(entities);
                return (T)(object)ec;
            }
            else if (typeof(T) == typeof(IEnumerable<Entity>))
            {
                return (T)(object)entities;
            }

            return (T)base.DataSource;
        }

        /// <summary>
        /// Refresh the contents of the gridview based on associated Entities and flags
        /// </summary>
        public override void Refresh()
        {
            if (entities != null)
            {
                var cols = GetTableColumns(entities);
                var data = GetDataTable(entities, cols);
                BindData(data);
                ArrangeColumns();
                FixColumnsFromLayout();
            }
            base.Refresh();
        }

        /// <summary>
        /// If you have a fetchxml (and maybe layoutxml too) you can set the DataSource by just call this method
        /// and send the fetchxml (and maybe layoutxml) which is automatically retrieved and displaying.
        /// </summary>
        /// <param name="fetchxml">FetchXML must be added, or just null the DataSource.</param>
        /// <param name="layoutxml">If this is added, it will show like layout in the Maker Portal (or in FXB)</param>
        /// <exception cref="Exception"></exception>
        /// <remarks>Remember that the Service must be set before calling this.</remarks>
        public void SetDataSource(string fetchxml, string layoutxml = null)
        {
            if (Service == null)
            {
                throw new Exception("Service is not specified.");
            }
            if (string.IsNullOrEmpty(fetchxml))
            {
                return;
            }
            var result = Service.RetrieveMultiple(new FetchExpression(fetchxml));
            MethodInvoker mi = delegate
            {
                LayoutXML = layoutxml;
                DataSource = result;
            };
            if (InvokeRequired)
            {
                Invoke(mi);
            }
            else
            {
                mi();
            }
        }

        #endregion Public methods

        #region Private event handler methods

        private void HandleClick(object sender, DataGridViewCellEventArgs e)
        {
            GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordClick);
        }

        private void HandleDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.RowIndex >= 0)
            {
                GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordDoubleClick);
            }
        }

        private void HandleCellEnter(object sender, DataGridViewCellEventArgs e)
        {
            GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordEnter);
        }

        private void HandleCellLeave(object sender, DataGridViewCellEventArgs e)
        {
            GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordLeave);
        }

        private void HandleCellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (EntityReferenceClickable)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    return;
                }
                var entity = GetRecordFromCellEvent(e);
                var row = Rows[e.RowIndex];
                var col = Columns[e.ColumnIndex];
                if (!entity.Contains(col.Name))
                {
                    return;
                }
                var value = entity[col.Name];
                if (value is EntityReference)
                {
                    var font = new Font(Font, FontStyle.Underline);
                    var cell = row.Cells[e.ColumnIndex];
                    cell.Style.Font = font;
                    Cursor = Cursors.Hand;
                }
            }
            GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordMouseEnter);
        }

        private void HandleCellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (EntityReferenceClickable)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    return;
                }
                var entity = GetRecordFromCellEvent(e);
                var row = Rows[e.RowIndex];
                var col = Columns[e.ColumnIndex];
                if (!entity.Contains(col.Name))
                {
                    return;
                }
                var value = entity[col.Name];
                if (value is EntityReference)
                {
                    var font = new Font(Font, FontStyle.Regular);
                    var cell = row.Cells[e.ColumnIndex];
                    cell.Style.Font = font;
                    Cursor = Cursors.Default;
                }
            }
            GetXRMRecordEventArgs(e).OnRecordEvent(this, RecordMouseLeave);
        }

        #endregion Private event handler methods

        #region Private methods

        private XRMRecordEventArgs GetXRMRecordEventArgs(DataGridViewCellEventArgs e)
        {
            Entity entity = GetRecordFromCellEvent(e);
            var attribute = e.ColumnIndex >= 0 ? Columns[e.ColumnIndex].Name : string.Empty;
            //            var args = new XRMRecordEventArgs(e.ColumnIndex, e.RowIndex, entity, attribute);
            var args = new XRMRecordEventArgs(entity, attribute);
            return args;
        }

        private Entity GetRecordFromCellEvent(DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 || !Columns.Contains("#entity"))
            {
                return null;
            }
            var rowno = e.RowIndex;
            var row = Rows[rowno];
            var entity = row.Cells["#entity"]?.Value as Entity;
            return entity;
        }

        private List<DataColumn> GetTableColumns(IEnumerable<Entity> entities)
        {
            var columns = new List<DataColumn>();
            if (!designedColumnsDetermined)
            {
                designedColumnsUsed = Columns.Count > 0;
                designedColumnsDetermined = true;
                if (designedColumnsUsed)
                {
                    designedColumns = new DataGridViewColumn[Columns.Count];
                    Columns.CopyTo(designedColumns, 0);
                }
            }
            if (designedColumnsUsed && cellsFromLayoutXML == null)
            {
                PopulateColumnsFromDesign(entities, columns);
            }
            else
            {
                Columns.Clear();
                columns.Add(new DataColumn("#no", typeof(int)) { Caption = "#", AutoIncrement = true, AutoIncrementSeed = 1 });
                columns.Add(new DataColumn("#id", typeof(Guid)) { Caption = "Id" });

                var attributes = new List<string>(columnOrder);
                if (showColumnsNotInColumnOrder || attributes.Count == 0)
                {
                    attributes.AddRange(entities
                        .SelectMany(e => e.Attributes)
                        .Select(a => a.Key)
                        .Distinct());
                }
                attributes.Distinct().ToList().ForEach(a =>
                {
                    // Force the column to be displayed if it's in the ColumnOrder list and we either want to display
                    // all the listed columns or it contains data
                    var force = columnOrder.Contains(a) && (showAllColumnsInColumnOrder || entities.Any(e => e.Contains(a)));

                    AddColumnForAttribute(entities, columns, a, force);
                });
            }
            if (!columns.Any(c => c.ColumnName.Equals("#entity")))
            {
                columns.Add(new DataColumn("#entity", typeof(Entity)));
            }
            return columns;
        }

        private void PopulateColumnsFromDesign(IEnumerable<Entity> entities, List<DataColumn> columns)
        {
            foreach (DataGridViewColumn viewcol in Columns)
            {
                if (viewcol.Name == "#entity")
                {
                    continue;
                }
                if (string.IsNullOrEmpty(viewcol.DataPropertyName))
                {
                    viewcol.DataPropertyName = viewcol.Name;
                }
                var attribute = viewcol.DataPropertyName;
                var dataColumn = CreateColumnForAttribute(entities, attribute, true);
                if (!string.IsNullOrEmpty(viewcol.DefaultCellStyle.Format))
                {
                    if (dataColumn.ExtendedProperties.Contains("Format"))
                    {
                        dataColumn.ExtendedProperties["Format"] = viewcol.DefaultCellStyle.Format;
                    }
                    else
                    {
                        dataColumn.ExtendedProperties.Add("Format", viewcol.DefaultCellStyle.Format);
                    }
                }
                dataColumn.Caption = viewcol.HeaderText;
                columns.Add(dataColumn);
            }
        }

        private DataColumn CreateColumnForAttribute(IEnumerable<Entity> entities, string attribute, bool force)
        {
            var value = GetFirstValueForAttribute(entities, attribute);
            if (value == null && !force)
            {
                return null;
            }
            var type = GetValueType(value);
            var dataColumn = new DataColumn(attribute, type);
            var meta = organizationService.GetAttribute(EntityName, attribute, value);
            dataColumn.ExtendedProperties.Add(_extendedMetaAttribute, meta);
            if (value is AliasedValue aliasvalue && aliasvalue.EntityLogicalName is string aliasentityname && !string.IsNullOrEmpty(aliasentityname))
            {
                var aliasentitymeta = organizationService.GetEntity(aliasentityname);
                dataColumn.ExtendedProperties.Add(_extendedMetaEntity, aliasentitymeta);
            }
            dataColumn.ExtendedProperties.Add("OriginalType", GetInnerValueType(value));
            if (meta is DateTimeAttributeMetadata && entities.Any(e => e.Contains(attribute) && e[attribute] is DateTime dtvalue && dtvalue.Millisecond > 0))
            {
                dataColumn.ExtendedProperties.Add("Format", "yyyy-MM-dd HH:mm:ss.fff");
            }
            return dataColumn;
        }

        private static Type GetInnerValueType(object value)
        {
            if (value is AliasedValue aliasedValue)
            {
                return GetInnerValueType(aliasedValue.Value);
            }
            return value != null ? value.GetType() : null;
        }

        private Type GetValueType(object value)
        {
            if (value == null)
            {
                return typeof(string);
            }
            if (showFriendlyNames || !ValueTypeIsFriendly(value))
            {
                return typeof(string);
            }
            var basevalue = EntitySerializer.AttributeToBaseType(value);
            if (basevalue == null)
            {
                return typeof(string);
            }
            return basevalue.GetType();
        }

        private bool ValueTypeIsFriendly(object value)
        {
            return value is Int32 || value is decimal || value is double || value is string || value is Money || value is DateTime;
        }

        private void AddColumnForAttribute(IEnumerable<Entity> entities, List<DataColumn> columns, string attribute, bool force)
        {
            if (columns.Any(c => c.ColumnName.Equals(attribute)))
            {   // Column already added for some reason
                return;
            }
            if (CreateColumnForAttribute(entities, attribute, force) is DataColumn dataColumn && dataColumn != null)
            {
                var meta = dataColumn.ExtendedProperties.ContainsKey(_extendedMetaAttribute) ? dataColumn.ExtendedProperties[_extendedMetaAttribute] as AttributeMetadata : null;
                if (meta?.IsPrimaryId == true && (!force || ShowIdColumn && meta.LogicalName == attribute))
                {
                    // Don't show the primary key column twice. Ignore the primary key column if:
                    // * `force` is false, i.e. the user isn't asking to see this column via the ColumnOrder property, OR
                    // * The standard ID column is being already shown (via ShowIdColumn) and this isn't an aliased version
                    //   An aliased column probably indicates an aggregate query and we want to show the aggregate (e.g. count)
                    //   as a different column to the ID of the record.
                    return;
                }
                if (showFriendlyNames &&
                   meta != null &&
                   meta.DisplayName != null &&
                   meta.DisplayName.UserLocalizedLabel != null)
                {
                    dataColumn.Caption = meta.DisplayName.UserLocalizedLabel.Label;
                    if (attribute.Contains("."))
                    {
                        if (dataColumn.ExtendedProperties.ContainsKey(_extendedMetaEntity) && dataColumn.ExtendedProperties[_extendedMetaEntity] is EntityMetadata aliasmeta)
                        {
                            dataColumn.Caption += $" ({aliasmeta.DisplayName.UserLocalizedLabel.Label})";
                        }
                        else
                        {
                            dataColumn.Caption = attribute.Split('.')[0] + " " + dataColumn.Caption;
                        }
                    }
                }
                else
                {
                    dataColumn.Caption = attribute;
                }
                columns.Add(dataColumn);
            }
        }

        private object GetFirstValueForAttribute(IEnumerable<Entity> entities, string attribute)
        {
            foreach (var entity in entities)
            {
                if (entity.Contains(attribute) && entity[attribute] != null)
                {
                    return entity[attribute];
                }
            }
            return null;
        }

        private DataTable GetDataTable(IEnumerable<Entity> entities, List<DataColumn> columns)
        {
            var dTable = new DataTable();
            dTable.Columns.AddRange(columns.ToArray());
            var filteredcols = columns.Cast<DataColumn>().Where(c => filterColumns == null || filterColumns.Contains(c.ColumnName.ToLowerInvariant())).ToList();

            foreach (var entity in entities)
            {
                var dRow = dTable.NewRow();
                foreach (DataColumn column in dTable.Columns)
                {
                    var col = column.ColumnName;
                    try
                    {
                        object value = null;
                        if (col == "#no")
                        {   // Sequence column
                            continue;
                        }
                        else if (col == "#id")
                        {
                            value = entity.Id;
                        }
                        else if (col == "#entity")
                        {
                            value = entity;
                        }
                        else if (entity.Contains(col) && entity[col] != null)
                        {
                            value = entity[col];
                            if (EntitySerializer.AttributeToBaseType(value) is DateTime dtvalue && showLocalTimes && (dtvalue).Kind == DateTimeKind.Utc)
                            {
                                value = dtvalue.ToLocalTime();
                            }
                            if (showFriendlyNames)
                            {
                                if (!ValueTypeIsFriendly(value) && column.ExtendedProperties.ContainsKey(_extendedMetaAttribute))
                                {
                                    value = EntitySerializer.AttributeToString(value, column.ExtendedProperties[_extendedMetaAttribute] as AttributeMetadata, column.ExtendedProperties["Format"] as string);
                                }
                                else
                                {
                                    value = EntitySerializer.AttributeToBaseType(value).ToString();
                                }
                            }
                            else
                            {
                                value = EntitySerializer.AttributeToBaseType(value);
                            }
                        }
                        if (value == null)
                        {
                            value = DBNull.Value;
                        }
                        dRow[column] = value;
                    }
                    catch
                    {
                        MessageBox.Show("Attribute " + col + " failed, value: " + entity[col].ToString());
                    }
                }
                if (FilterIncludeRow(dRow, filteredcols))
                {
                    dTable.Rows.Add(dRow);
                }
            }
            return dTable;
        }

        private bool FilterIncludeRow(DataRow dRow, List<DataColumn> filtercolumns)
        {
            if (string.IsNullOrEmpty(filterText))
            {
                return true;
            }
            foreach (var column in filtercolumns)
            {
                var cellvalue = dRow[column];
                if (cellvalue.ToString().ToLowerInvariant().Contains(filterText))
                {
                    return true;
                }
            }
            return false;
        }

        private void BindData(DataTable dTable)
        {
            SuspendLayout();
            base.DataSource = dTable;
            VerifySupportingColumn("#entity");
            foreach (DataGridViewColumn col in Columns)
            {
                var datacolumn = dTable.Columns[col.DataPropertyName];
                col.HeaderText = datacolumn.Caption;
                var type = datacolumn.DataType;
                if (datacolumn.ExtendedProperties.ContainsKey("OriginalType"))
                {
                    type = datacolumn.ExtendedProperties["OriginalType"] as Type;
                }
                if (type == typeof(int) || type == typeof(decimal) || type == typeof(double) || type == typeof(Money) || (type == typeof(OptionSetValue) && !showFriendlyNames))
                {
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                if (datacolumn.ExtendedProperties.ContainsKey("Format"))
                {
                    col.DefaultCellStyle.Format = datacolumn.ExtendedProperties["Format"] as string;
                }
                if (datacolumn.ColumnName == "#no")
                {
                    col.Visible = showIndexColumn;
                }
                if (datacolumn.ColumnName == "#id")
                {
                    col.Visible = showIdColumn;
                }
                if (datacolumn.ColumnName == "#entity")
                {
                    col.Visible = false;
                }
            }
            if (AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.None && AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill)
            {
                AutoResizeColumns(AutoSizeColumnsMode);
            }
            ResumeLayout();
        }

        private void VerifySupportingColumn(string col)
        {
            if (!Columns.Contains(col))
            {
                var newcol = new DataGridViewTextBoxColumn
                {
                    Name = col,
                    DataPropertyName = col,
                    HeaderText = col,
                    ReadOnly = true
                };
                Columns.Add(newcol);
            }
        }

        private void ArrangeColumns()
        {
            if (columnOrder?.Length == 0 || designedColumnsUsed)
            {
                return;
            }
            var pos = 2;
            foreach (var attribute in columnOrder)
            {
                if (Columns.Contains(attribute))
                {
                    Columns[attribute].DisplayIndex = pos;
                    pos++;
                }
            }
        }

        private Dictionary<string, int> GetCellsFromLayoutXML(string layoutxml)
        {
            string GetCellName(XmlNode node)
            {
                if (node != null && node.Attributes != null && node.Attributes["name"] is XmlAttribute attr)
                {
                    return attr.Value;
                }
                return string.Empty;
            }
            int GetCellWidth(XmlNode node)
            {
                if (node != null && node.Attributes != null && node.Attributes["width"] is XmlAttribute attr)
                {
                    if (int.TryParse(attr.Value, out int width))
                    {
                        return width;
                    }
                }
                return 0;
            }

            if (!string.IsNullOrEmpty(layoutxml))
            {
                var doc = new XmlDocument();
                doc.LoadXml(layoutxml);
                if (doc.SelectSingleNode("grid") is XmlElement grid)
                {
                    var cells = grid.SelectSingleNode("row")?
                        .ChildNodes.Cast<XmlNode>()
                        .Where(n => n.Name == "cell")
                        .Select(c => new KeyValuePair<string, int>(GetCellName(c), GetCellWidth(c)))
                        .ToDictionary(c => c.Key, c => c.Value);
                    return cells?.Count > 0 ? cells : null;
                }
            }
            return null;
        }

        private void FixColumnsFromLayout()
        {
            if (cellsFromLayoutXML != null)
            {
                var cellnames = cellsFromLayoutXML.Keys.ToList();
                cellnames.Where(c => !Columns.Contains(c)).ToList().ForEach(c => Columns.Add(c, c));
                foreach (var column in Columns?.Cast<DataGridViewColumn>())
                {
                    if (cellsFromLayoutXML.ContainsKey(column.Name))
                    {
                        var cell = cellsFromLayoutXML[column.Name];
                        column.DisplayIndex = Math.Min(cellnames.IndexOf(column.Name), Columns.Count - 1);
                        column.Width = cell;
                        column.Visible = cell > 0;
                    }
                    else
                    {
                        column.Visible = false;
                    }
                }
            }
        }

        #endregion Private methods
    }
}