
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
using Microsoft.Win32;


namespace Mms_Metaverse
{
    /// <summary>
    /// Summary description for MVExtensionObject.
    /// </summary>
    public class MVExtensionObject : IMVSynchronization
    {
        public MVExtensionObject()
        {

        }

        void IMVSynchronization.Initialize()
        {

        }

        void IMVSynchronization.Terminate()
        {

        }

        void IMVSynchronization.Provision(MVEntry mventry)
        {
            //get our provisioning ma from the db
            string provisioningMAName = FIMConfiguration.GetProvisioningMA()["ma_name"].ToString();

            XmlDocument xmldoc = FIMConfiguration.GetConfigXML(provisioningMAName, "private_configuration_xml");
            XmlNodeList attributes = xmldoc.SelectNodes("//MAConfig/parameter-values/parameter");

            //loop through each MA/object type selected
            foreach (XmlNode attrib in attributes)
            {
                string param = attrib.Attributes["name"].Value;
                string maName = param.Substring(0, param.LastIndexOf(" - "));
                string objectType = param.Substring(param.LastIndexOf(" - ") + 3);

                //if enabled, provision it
                if (attrib.InnerText.Equals("1"))
                {
                    //our ma has been enabled for provisioning, create a new csentry and add initial flows
                    ConnectedMA ma = mventry.ConnectedMAs[maName];

                    if (ma.Connectors.Count == 0)
                    {
                        CSEntry csentry = ma.Connectors.StartNewConnector(objectType);

                        //go and get the real anchor info, our provisioning ma
                        //uses a generic anchor to ensure tha flows can be
                        //defined for the actual anchor
                        XmlDocument maSchemaConfig = FIMConfiguration.GetConfigXML(maName, "dn_construction_xml");
                        XmlNode maSchemaRoot = maSchemaConfig.FirstChild;

                        //get dn for the object
                        List<string> anchors = new List<string>();
                        if (maSchemaRoot.FirstChild.Name.Equals("attribute", StringComparison.InvariantCultureIgnoreCase))
                        {
                            XmlNodeList anchorList = maSchemaConfig.SelectNodes("//dn-construction/attribute");

                            foreach (XmlNode anchor in anchorList)
                            {
                                anchors.Add(anchor.InnerText);
                            }
                        }
                        else
                        {
                            XmlNodeList anchorList = maSchemaConfig.SelectNodes("//dn-construction/dn[@object-type='" + objectType + "']/attribute");

                            foreach (XmlNode anchor in anchorList)
                            {
                                anchors.Add(anchor.InnerText);
                            }
                        }

                        //our export schema defines the initial attributes to flow
                        XmlDocument xmlFlows = FIMConfiguration.GetConfigXML(provisioningMAName, "export_attribute_flow_xml");
                        XmlNodeList flows = xmlFlows.SelectNodes("//export-attribute-flow/export-flow-set[@cd-object-type='" + maName + "']/export-flow");

                        foreach (XmlNode flow in flows)
                        {
                            //get the mapping for each flow defined and provision an initial flow
                            string csAttribName = flow.Attributes["cd-attribute"].Value;
                            csAttribName = csAttribName.Substring(csAttribName.LastIndexOf(" - ") + 3);

                            XmlNode mappingNode = flow.FirstChild;
                            string mappingType = mappingNode.Name;
                            string flowValue = null;
                            ValueCollection flowValues = null;

                            switch (mappingType)
                            {
                                case "direct-mapping":

                                    string mvAttribName = mappingNode.FirstChild.InnerText;

                                    if (mventry[mvAttribName].IsPresent)
                                    {
                                        if (mventry[mvAttribName].IsMultivalued)
                                        {
                                            flowValues = mventry[mvAttribName].Values;
                                        }
                                        else
                                        {
                                            //TODO: convert this to its proper type if necessary (i.e. int, boolean, etc)
                                            flowValue = mventry[mvAttribName].Value;
                                        }
                                    }
                                    break;
                                case "constant-mapping":
                                    flowValue = mappingNode.FirstChild.InnerText;
                                    break;
                                case "scripted-mapping":
                                    break;
                                default:
                                    throw new Exception("Unexpected mapping type encountered. Only Direct, Constant and Advanced/Scripted flows are allowed. Check flow rules and try again.");
                            }

                            if (flowValue != null || flowValues != null)
                            {
                                //calc dn if necessary
                                if (csAttribName.Equals("dn", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    //are we safe to assume that if we are calculating dn, we must
                                    //be using flowValue and not flowValues?
                                    string rdn = flowValue.ToString();
                                    ReferenceValue dn = ma.EscapeDNComponent(rdn);
                                    csentry.DN = dn;
                                }
                                else
                                {
                                    try
                                    {
                                        if (flowValue != null)
                                        {
                                            csentry[csAttribName].Values.Add(flowValue);
                                        }
                                        else if (flowValues != null)
                                        {
                                            csentry[csAttribName].Values.Add(flowValues);
                                        }
                                    }
                                    catch (InvalidOperationException ex)
                                    {
                                        if (!ex.Message.Equals("attribute " + csAttribName + " is read-only", StringComparison.InvariantCultureIgnoreCase))
                                        {
                                            throw;
                                        }
                                        else
                                        {
                                            //our anchor attribute is read only, set a temporary dn
                                            if (anchors.Contains(csAttribName))
                                            {
                                                ReferenceValue dn = ma.EscapeDNComponent(Guid.NewGuid().ToString());
                                                csentry.DN = dn;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        //do we want to throw an error now if any writeable anchor attributes have not been set??
                        //otherwise they will get one on export, we will leave it that way for now

                        csentry.CommitNewConnector();
                    }
                }
            }


        }

        bool IMVSynchronization.ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {

            throw new EntryPointNotImplementedException();
        }

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

            //we want to fetch schema for all MAs, that way configuration wont get lost if an item is 
            //unselected, and the checkbox can become simply an activate/deactivate switch
            DataSet mas = FIMConfiguration.GetManagementAgents(null);

            foreach (DataRow ma in mas.Tables["Config"].Rows)
            {
                string maName = ma["ma_name"].ToString();
                string maType = ma["ma_type"].ToString();
                string maList = ma["ma_listname"].ToString();

                if (maType.Equals("FIM", StringComparison.InvariantCultureIgnoreCase) ||
                    maList.Equals("Provisioning Management Agent (Insight)", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                //create a new schema type based on the ma
                type = Microsoft.MetadirectoryServices.SchemaType.Create(maName, false);

                //add a generic Anchor Attribute to allow user to add flows for the actual anchor
                type.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("Anchor", AttributeType.String));

                //we must preface each attribute with the MA name to make it unique across the schema allowing for 
                //an attribute in two different MAs with different data types, etc.

                //our data will come back as XML data, we will need to parse it for what we need                        
                XmlDocument xmldoc = FIMConfiguration.GetConfigXML(maName, "ma_schema_xml");

                XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(xmldoc.NameTable);
                xmlnsManager.AddNamespace("dsml", "http://www.dsml.org/DSML");
                xmlnsManager.AddNamespace("ms-dsml", "http://www.microsoft.com/MMS/DSML");

                XmlNodeList attributes = xmldoc.SelectNodes("//dsml:directory-schema/dsml:attribute-type", xmlnsManager);

                //add each attribute found to the schema
                foreach (XmlNode attrib in attributes)
                {
                    string oid = attrib.SelectSingleNode("./dsml:syntax", xmlnsManager).InnerText;
                    type.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(maName + " - " + attrib.Attributes["id"].Value, FIMConfiguration.GetDataType(oid)));
                }

                schema.Types.Add(type);
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
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateLabelParameter("Enable provisioning for the following types:"));

                    //look up MAs and list them as check boxes for provisioning enablement
                    DataSet mas = FIMConfiguration.GetManagementAgents(null);

                    foreach (DataRow ma in mas.Tables["Config"].Rows)
                    {
                        string maName = ma["ma_name"].ToString();
                        string maType = ma["ma_type"].ToString();
                        string maList = ma["ma_listname"].ToString();
                        
                        if (maType.Equals("FIM", StringComparison.InvariantCultureIgnoreCase) ||
                            maList.Equals("Provisioning Management Agent (Insight)", StringComparison.InvariantCultureIgnoreCase))
                        {
                            continue;
                        }

                        //our data will come back as XML data, we will need to parse it for what we need                        
                        XmlDocument xmldoc = FIMConfiguration.GetConfigXML(maName, "ma_schema_xml");

                        XmlNamespaceManager xmlnsManager = new XmlNamespaceManager(xmldoc.NameTable);
                        xmlnsManager.AddNamespace("dsml", "http://www.dsml.org/DSML");
                        xmlnsManager.AddNamespace("ms-dsml", "http://www.microsoft.com/MMS/DSML");

                        //TODO:  what happens when a sql column defines the object type?
                        XmlNodeList objectTypes = xmldoc.SelectNodes("//dsml:directory-schema/dsml:class", xmlnsManager);

                        foreach (XmlNode ot in objectTypes)
                        {
                            //add the object type as a selection
                            ConfigParameterDefinition conf = ConfigParameterDefinition.CreateCheckBoxParameter(maName + " - " + ot.Attributes["id"].Value.Replace(" - ", " _ "));
                            configParametersDefinitions.Add(conf);
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
                //set capabilities of the provisioning ma
                MACapabilities myCapabilities = new MACapabilities();

                myCapabilities.ConcurrentOperation = true;
                myCapabilities.ObjectRename = true;
                myCapabilities.DeleteAddAsReplace = true;
                myCapabilities.DeltaImport = false;
                myCapabilities.DistinguishedNameStyle = MADistinguishedNameStyle.None;
                myCapabilities.Normalizations = MANormalizations.None;
                myCapabilities.ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation;

                return myCapabilities;
            }
        }

        #region Interface Methods for Import - DO NOT USE OR REMOVE

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

    }

    public static class FIMConfiguration
    {

        static string server = ".";
        static string database = "FIMSynchronization";

        static FIMConfiguration()
        {
            //get synch db info from the registry
            RegistryKey key = Registry.LocalMachine.CreateSubKey("SYSTEM\\CurrentControlSet\\Services\\FIMSynchronizationService\\Parameters");
            if (key != null)
            {
                string instance = key.GetValue("SQLInstance").ToString();
                
                server = key.GetValue("Server").ToString();
                database = key.GetValue("DBName").ToString();

                if (instance != "")
                {
                    server = "\\" + instance;
                }
            }
            key.Close();

        }

        /// <summary>
        /// Gets xml configuraiton information about an MA
        /// </summary>
        /// <param name="server"></param>
        /// <param name="database"></param>
        /// <param name="maName"></param>
        /// <param name="configSelection"></param>
        /// <returns></returns>
        internal static XmlDocument GetConfigXML(string maName, string configSelection)
        {
            XmlDocument xmlDoc = new XmlDocument();

            //connect to db and pull MA info
            DataSet ds = GetManagementAgents(maName);
            
            //load data into an xml document
            if (ds != null && ds.Tables["Config"] != null && ds.Tables["Config"].Rows != null && ds.Tables["Config"].Rows.Count > 0)
            {
                DataRow provConfig = ds.Tables["Config"].Rows[0];

                //our data will come back as XML data, we will need to parse it for what we need                        
                xmlDoc.LoadXml(provConfig[configSelection].ToString());
            }

            return xmlDoc;
        }

        /// <summary>
        /// Gets all Management Agents configured
        /// </summary>
        /// <param name="server"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        internal static DataSet GetManagementAgents(string maName)
        {
            string query = maName == null ? "" : " where ma_name = '" + maName.Replace("'", "''") + "'";

            //connect to the db
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;

            //query the ma config info
            string cmdText = "Select * from mms_management_agent with (nolock) " + query;
            cmd.CommandText = cmdText;
            cmd.Connection = conn;
            
            //return data found as a dataset
            SqlDataAdapter da = new SqlDataAdapter(cmd);
            DataSet ds = new DataSet();
            da.Fill(ds, "Config");
            return ds;
        }

        /// <summary>
        /// Gets the data row for the Provisioning MA
        /// </summary>
        /// <returns></returns>
        internal static DataRow GetProvisioningMA()
        {
            //connect to the db
            string connString = ("Server = '" + server + "';Initial Catalog='" + database + "';Integrated Security=True");
            SqlConnection conn = new SqlConnection(connString);
            SqlCommand cmd = new SqlCommand();
            cmd.CommandType = CommandType.Text;

            //query the ma config info
            string cmdText = "Select top 1 * from mms_management_agent with (nolock) where ma_listname = 'Provisioning Management Agent (Insight)'";
            cmd.CommandText = cmdText;
            cmd.Connection = conn;

            //return data found as a dataset
            SqlDataAdapter da = new SqlDataAdapter(cmd);
            DataSet ds = new DataSet();
            da.Fill(ds, "Config");

            if (ds.Tables["Config"].Rows.Count < 1)
            {
                throw new Exception("Unable to locate the Provisioning MA, please check your settings and try again.");
            }
            else if(ds.Tables["Config"].Rows.Count > 1)
            {
                throw new Exception("Multiple Provisioning MAs were located, ensure only one Provisioning MA has been configured and try again.");
            }

            return ds.Tables["Config"].Rows[0];
        }

        /// <summary>
        /// Converts the oid into a .NET object type
        /// </summary>
        /// <param name="oid"></param>
        /// <returns></returns>
        internal static AttributeType GetDataType(string oid)
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

    }
}
