
using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.MetadirectoryServices;
using System.Data.SqlClient;
using System.Data;


namespace Mms_Metaverse
{
    /// <summary>
    /// Summary description for MVExtensionObject.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {
        public MVExtensionObject()
        {
            //
            // TODO: Add constructor logic here
            //
        }

        void IMVSynchronization.Initialize()
        {
            //
            // TODO: Add initialization logic here
            //
        }

        void IMVSynchronization.Terminate()
        {
            //
            // TODO: Add termination logic here
            //
        }

        void IMVSynchronization.Provision(MVEntry mventry)
        {
            //TODO: get this info from the registry
            string server = ".";
            string db = "FIMSynchronization";

            //get our provisioning ma from the db
            //TODO: hard code MA name for now, we will need to figure out how to 
            //programmatically get this later
            DataSet config = this.GetMAConfig(server, db, "FIM Provisioning");

            if (config != null && config.Tables["Config"] != null && config.Tables["Config"].Rows != null && config.Tables["Config"].Rows.Count > 0)
            {
                DataRow maConfig = config.Tables["Config"].Rows[0];

                //our data will come back as XML data, we will need to parse it for what we need                        
                XmlDocument xmldoc = new XmlDocument();
                xmldoc.LoadXml(maConfig["private_configuration_xml"].ToString());

                XmlNodeList attributes = xmldoc.SelectNodes("//MAConfig/parameter-values");

                //loop through each MA/object type selected
                foreach (XmlNode attrib in attributes)
                {
                    string maName = attrib.Attributes["name"].Value;
                    maName = maName.Substring(0, maName.Length - " - Enable Provisioning".Length);

                    if (attrib.InnerText.Equals("1"))
                    {
                        //our ma has been enabled for provisioning, create a new csentry and add initial flows
                        ConnectedMA ma = mventry.ConnectedMAs[maName];

                        //TODO: add support fo cs object type
                        CSEntry csentry = ma.Connectors.StartNewConnector("");

                        //TODO: calc dn if necessary
                        string rdn = "";
                        ReferenceValue dn = ma.EscapeDNComponent(rdn);

                        //our export schema defines the initial attributes to flow
                        DataSet exportFlows = this.GetExportSchema(server, db, maName);

                        XmlDocument xmlFlows = new XmlDocument();
                        xmlFlows.LoadXml(maConfig["export_attribute_flow_xml"].ToString());

                        XmlNodeList flows = xmldoc.SelectNodes("//export-attribute-flow/export-flow-set[@cd-object-type='" + maName + "']");

                        foreach (XmlNode flow in flows)
                        {
                            //TODO: take into account the other flow types (i.e. constant and advanced)
                            //TODO: check for csentry and mventry data types
                            //TODO: check for multivalued attributes
                            string csAttribName = flow.Attributes["cd-attribute"].Value;
                            csAttribName = csAttribName.Substring((maName + " - ").Length);

                            string mvAttribName = flow.SelectSingleNode("./direct-mapping/src-attribute").InnerText;

                            csentry[csAttribName].Value = mventry[mvAttribName].Value;
                        }

                        csentry.DN = dn;
                        csentry.CommitNewConnector();
                    }
                }
            }

        }

        bool IMVSynchronization.ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            //
            // TODO: Add MV deletion logic here
            //
            throw new EntryPointNotImplementedException();
        }

        #region private methods
        private DataSet GetExportSchema(string server, string database, string maName)
        {
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;
            string cmdText = "Select export_attribute_flow_xml from mms_management_agent with (nolock) where ma_name = '" + maName.Replace("'", "''") + "'";
            cmd.CommandText = cmdText;
            cmd.Connection = conn;
            SqlDataAdapter adapter = new SqlDataAdapter(cmd);
            DataSet da = new DataSet();
            adapter.Fill(da, "Schema");
            return da;
        }

        private DataSet GetMAConfig(string server, string database, string maName)
        {
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;
            string cmdText = "Select private_configuration_xml from mms_management_agent with (nolock) where ma_name = '" + maName.Replace("'", "''") + "'";
            cmd.CommandText = cmdText;
            cmd.Connection = conn;
            SqlDataAdapter adapter = new SqlDataAdapter(cmd);
            DataSet da = new DataSet();
            adapter.Fill(da, "Config");
            return da;
        }
        #endregion
    }

    public class MAExtensionObject :
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2GetCapabilities,
        IMAExtensible2CallImport
    {

        public Schema GetSchema(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters)
        {
            Schema schema = Schema.Create();
            Microsoft.MetadirectoryServices.SchemaType type;

            //TODO:  get these from the registry
            string server = ".";
            string db = "FIMSynchronization";

            //we want to fetch schema for all MAs, that way configuration wont get lost if an item is 
            //unselected, and the checkbox can become simply an activate/deactivate switch
            DataSet mas = this.GetManagementAgents(server, db);

            foreach (DataRow ma in mas.Tables["MAs"].Rows)
            {
                string maName = ma["ma_name"].ToString();

                DataSet maData = this.GetMASchema(server, db, maName);

                if (maData != null && maData.Tables["Schema"] != null && maData.Tables["Schema"].Rows != null && maData.Tables["Schema"].Rows.Count > 0)
                {
                    DataRow maSchema = maData.Tables["Schema"].Rows[0];

                    type = Microsoft.MetadirectoryServices.SchemaType.Create(maName, false);

                    //add a generic Anchor Attribute to allow user to add flows for the actual anchor
                    type.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("Anchor", AttributeType.String));

                    //we must preface each attribute with the MA name to make it unique across the schema allowing for 
                    //an attribute in two different MAs with different data types, etc.

                    //our data will come back as XML data, we will need to parse it for what we need                        
                    XmlDocument xmldoc = new XmlDocument();
                    xmldoc.LoadXml(maSchema["ma_schema_xml"].ToString());

                    XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(xmldoc.NameTable);
                    xmlnsManager.AddNamespace("dsml", "http://www.dsml.org/DSML");
                    xmlnsManager.AddNamespace("ms-dsml", "http://www.microsoft.com/MMS/DSML");

                    XmlNodeList attributes = xmldoc.SelectNodes("//dsml:directory-schema/dsml:attribute-type", xmlnsManager);

                    //add each attribute found to the schema
                    foreach (XmlNode attrib in attributes)
                    {
                        string oid = attrib.SelectSingleNode("./dsml:syntax", xmlnsManager).InnerText;
                        type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(maName + " - " + attrib.Attributes["id"].Value, GetDataType(oid)));
                    }

                    schema.Types.Add(type);
                }
                else
                {
                    throw new Exception("Unable to locate the schema for Management Agent " + maName);
                }
            }

            return schema;

        }

        public System.Collections.Generic.IList<ConfigParameterDefinition> GetConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    //we have to configure the MAs being seletected in the Connectivity page so that the information
                    //will be available to us when fetching the schema for the items selected 

                    //TODO: get this info from the registry
                    string server = ".";
                    string db = "FIMSynchronization";

                    //TODO:  if we are updating the MA in the GUI our Provisioning MA will
                    //appear in the list, is there a way we can filter it out?
                    //Whatever we figure out for this will have to apply to the provisioning
                    //code as well - we have to be able to definitively pull the MA from db

                    //look up MAs and list them as check boxes for provisioning enablement
                    DataSet mas = this.GetManagementAgents(server, db);

                    foreach (DataRow ma in mas.Tables["MAs"].Rows)
                    {
                        string maName = ma["ma_name"].ToString();

                        //add the ma as a selection
                        configParametersDefinitions.Add(ConfigParameterDefinition.CreateLabelParameter(maName + " - Enable provisioning for the following types:"));

                        DataSet schema = this.GetMASchema(server, db, maName);
                        DataRow maSchema = schema.Tables["Schema"].Rows[0];

                        //our data will come back as XML data, we will need to parse it for what we need                        
                        XmlDocument xmldoc = new XmlDocument();
                        xmldoc.LoadXml(maSchema["ma_schema_xml"].ToString());

                        XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(xmldoc.NameTable);
                        xmlnsManager.AddNamespace("dsml", "http://www.dsml.org/DSML");
                        xmlnsManager.AddNamespace("ms-dsml", "http://www.microsoft.com/MMS/DSML");

                        //TODO:  what happens when a sql column defines the object type?
                        XmlNodeList objectTypes = xmldoc.SelectNodes("//dsml:directory-schema/dsml:class", xmlnsManager);

                        foreach (XmlNode ot in objectTypes)
                        {
                            //add the object type as a selection
                            configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ot.Attributes["id"].Value));
                        }

                        //TODO:  what happens to the UI when we have a alot of entries?
                        configParametersDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    }

                    break;
                case ConfigParameterPage.Global:
                    break;
                case ConfigParameterPage.Partition:
                    break;
                case ConfigParameterPage.RunStep:
                    break;
            }

            return configParametersDefinitions;

        }

        public ParameterValidationResult ValidateConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            ParameterValidationResult myResults = new ParameterValidationResult();

            return myResults;

        }

        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities myCapabilities = new MACapabilities();

                myCapabilities.ConcurrentOperation = true;
                myCapabilities.ObjectRename = true;
                myCapabilities.DeleteAddAsReplace = true;
                myCapabilities.DeltaImport = false;
                myCapabilities.DistinguishedNameStyle = MADistinguishedNameStyle.None;
                //myCapabilities.ExportType = MAExportType.AttributeReplace;
                //myCapabilities.NoReferenceValuesInFirstExport = false;
                myCapabilities.Normalizations = MANormalizations.None;
                //myCapabilities.ExportPasswordInFirstPass = true;
                //myCapabilities.FullExport = false;
                myCapabilities.ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation;

                return myCapabilities;
            }
        }

        #region Interface Methods for Import - NOT USED

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            throw new NotImplementedException();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            throw new NotImplementedException();
        }

        public int ImportDefaultPageSize
        {
            get
            {
                return 0;
            }

        }

        public int ImportMaxPageSize
        {
            get
            {
                return 0;
            }

        }

        public OpenImportConnectionResults OpenImportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Private Methods

        private DataSet GetMASchema(string server, string database, string maName)
        {
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;
            string cmdText = "Select ma_schema_xml from mms_management_agent with (nolock) where ma_name = '" + maName.Replace("'", "''") + "'";
            cmd.CommandText = cmdText;
            cmd.Connection = conn;
            SqlDataAdapter adapter = new SqlDataAdapter(cmd);
            DataSet da = new DataSet();
            adapter.Fill(da, "Schema");
            return da;
        }

        private DataSet GetManagementAgents(string server, string database)
        {
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;
            string cmdText = "Select ma_name from mms_management_agent with (nolock)";
            cmd.CommandText = cmdText;
            cmd.Connection = conn;
            SqlDataAdapter adapter = new SqlDataAdapter(cmd);
            DataSet da = new DataSet();
            adapter.Fill(da, "MAs");
            return da;
        }

        private AttributeType GetDataType(string oid)
        {

            //FIM currently implements the following types: String, Integer, Binary, Boolean, Reference
            //We will map each of the ldap oids used in the schema to one of these types
            switch (oid)
            {
                case "1.3.6.1.4.1.1466.115.121.1.27":
                    return AttributeType.Integer;

                case "1.3.6.1.4.1.1466.115.121.1.5":
                    return AttributeType.Binary;

                case "1.3.6.1.4.1.1466.115.121.1.7":
                    return AttributeType.Boolean;

                case "":
                    return AttributeType.Reference;

                default:
                    return AttributeType.String;
            }

            //TODO: ensure that all of the possible types get mapped above
            //1.3.6.1.4.1.1466.115.121.1.3 = AttributeTypeDescription
            //1.3.6.1.4.1.1466.115.121.1.5 = BinarySyntax
            //1.3.6.1.4.1.1466.115.121.1.6 = BitstringSyntax
            //1.3.6.1.4.1.1466.115.121.1.7 = BooleanSyntax
            //1.3.6.1.4.1.1466.115.121.1.8 = CertificateSyntax
            //1.3.6.1.4.1.1466.115.121.1.9 = CertificateListSyntax
            //1.3.6.1.4.1.1466.115.121.1.10 = CertificatePairSyntax
            //1.3.6.1.4.1.1466.115.121.1.11 = CountryStringSyntax
            //1.3.6.1.4.1.1466.115.121.1.12 = DistinguishedNameSyntax
            //1.3.6.1.4.1.1466.115.121.1.14 = DeliveryMethod
            //1.3.6.1.4.1.1466.115.121.1.15 = DirectoryStringSyntax
            //1.3.6.1.4.1.1466.115.121.1.16 = DITContentRuleSyntax
            //1.3.6.1.4.1.1466.115.121.1.17 = DITStructureRuleDescriptionSyntax
            //1.3.6.1.4.1.1466.115.121.1.21 = EnhancedGuide
            //1.3.6.1.4.1.1466.115.121.1.22 = FacsimileTelephoneNumberSyntax
            //1.3.6.1.4.1.1466.115.121.1.23 = FaximageSyntax
            //1.3.6.1.4.1.1466.115.121.1.24 = GeneralizedTimeSyntax
            //1.3.6.1.4.1.1466.115.121.1.26 = IA5StringSyntax
            //1.3.6.1.4.1.1466.115.121.1.27 = IntegerSyntax
            //1.3.6.1.4.1.1466.115.121.1.28 = JPegImageSyntax
            //1.3.6.1.4.1.1466.115.121.1.30 = MatchingRuleDescriptionSyntax
            //1.3.6.1.4.1.1466.115.121.1.31 = MatchingRuleUseDescriptionSyntax
            //1.3.6.1.4.1.1466.115.121.1.33 = MHSORAddressSyntax
            //1.3.6.1.4.1.1466.115.121.1.34 = NameandOptionalUIDSyntax
            //1.3.6.1.4.1.1466.115.121.1.35 = NameFormSyntax
            //1.3.6.1.4.1.1466.115.121.1.36 = NumericStringSyntax
            //1.3.6.1.4.1.1466.115.121.1.37 = ObjectClassDescriptionSyntax
            //1.3.6.1.4.1.1466.115.121.1.38 = OIDSyntax
            //1.3.6.1.4.1.1466.115.121.1.39 = OtherMailboxSyntax
            //1.3.6.1.4.1.1466.115.121.1.40 = OctetString
            //1.3.6.1.4.1.1466.115.121.1.41 = PostalAddressSyntax
            //1.3.6.1.4.1.1466.115.121.1.43 = PresentationAddressSyntax
            //1.3.6.1.4.1.1466.115.121.1.44 = PrintablestringSyntax
            //1.3.6.1.4.1.1466.115.121.1.49 = SupportedAlgorithm
            //1.3.6.1.4.1.1466.115.121.1.50 = TelephonenumberSyntax
            //1.3.6.1.4.1.1466.115.121.1.51 = TeletexTerminalIdentifier
            //1.3.6.1.4.1.1466.115.121.1.52 = TelexNumber
            //1.3.6.1.4.1.1466.115.121.1.53 = UTCTimeSyntax
            //1.3.6.1.4.1.1466.115.121.1.54 = LDAPSyntaxDescriptionSyntax


        }

        #endregion


    }
}
