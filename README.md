# FIMMV
The Last FIM Metaverse Extension You Will Ever Need

PROJECT DESCRIPTION
 
This is a Forefront Identity Manager Metaverse Extension that will provide codeless provisioning by allowing the user to create a special MA in which the flows defined become the initial mappings used during provisioning.

This project became a natural extension to the work I did for the Last FIM Workflow You Will Ever Need http://www.apollojack.com/2012/02/last-fim-workflow-you-will-ever-need.html and the Last FIM Management Agent Rules Extension You Will Ever Need http://www.apollojack.com/2013/02/the-last-fim-management-agent-rules.html. This project is still in its infancy and has only been unit tested, but I wanted to get it out to you in case it might help anyone, as well as, get improvements from the community.

More details can be found on my blog article The Last FIM Metaverse Extension You Will Ever Need http://www.apollojack.com/2013/03/the-last-fim-metaverse-extension-you.html

INSTALLATION

1. Rebuild the code using the appropriate Microsoft.MetadirectoryServicesEx assemblies for your FIM environment
2. Make sure the project dll gets placed into the FIM Extensions Folder
3. Close the FIM Synch Client and then copy the Provisioning MA.xml file to the FIM Install location under 
   ~\Microsoft Forefront Identity Manager\2010\Synchronization Service\UIShell\XMLs\PackagedMAs
4. Create a new MA of type Provisioning Management Agent (Insight)
5. Do not create any run profiles for this MA, it will never be run in that fashion. The rules defined in the MA will become the initial flow rules used when provisioning is executing.
 
KNOWN ISSUES

If an MA configured for Provisioning is renamed, mappings will be lost and the Provisioning MA will have to updated
Advanced flow rules currently arent supported, only direct and constant flows
