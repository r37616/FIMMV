**INSTALLATION**
1. Rebuild the code using the appropriate Microsoft.MetadirectoryServicesEx assemblies for your FIM environment
2. Make sure the project dll gets placed into the FIM Extensions Folder
3. Close the FIM Synch Client and then copy the Provisioning MA.xml file to the FIM Install location under 
   ~\Microsoft Forefront Identity Manager\2010\Synchronization Service\UIShell\XMLs\PackagedMAs
4. Create a new MA of type Provisioning Management Agent (Insight)
5. Do not create any run profiles for this MA, it will never be run in that fashion.  The rules defined in the MA
   will become the initial flow rules used when provisioning is executing.


**KNOWN ISSUES**
1. If an MA configured for Provisioning is renamed, mappings will be lost and the Provisioning MA will have to updated
2. Advanced flow rules currently arent supported, only direct and constant flows