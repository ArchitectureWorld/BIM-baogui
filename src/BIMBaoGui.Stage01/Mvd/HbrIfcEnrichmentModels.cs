using System;
using System.Collections.Generic;

namespace BIMBaoGui.Stage01.Mvd
{
  internal enum HbrIfcOperationKind
  {
    CandidateClone,
    GraphIndexFullPass,
    GraphValidation,
    BatchInspection,
    IndexedFieldLookup,
    SectionBoundaryScan,
    MaximumIdScan,
    ForeignRelationshipRescan,
    CommitClone,
    CommitTransfer,
    DocumentEntityEnumeration
  }

  internal sealed class HbrIfcOperationEvent
  {
    public HbrIfcOperationEvent(
      HbrIfcOperationKind kind,
      int itemCount = 1)
    {
      if (itemCount < 0)
        throw new ArgumentOutOfRangeException(nameof(itemCount));
      Kind = kind;
      ItemCount = itemCount;
    }

    public HbrIfcOperationKind Kind { get; }
    public int ItemCount { get; }
  }

  internal interface IHbrIfcOperationObserver
  {
    void Observe(HbrIfcOperationEvent operation);
  }

  internal static class HbrIfcOwnerStrategies
  {
    public const string GlobalId = "GLOBAL_ID";
    public const string SingleEntityByType = "SINGLE_ENTITY_BY_TYPE";
  }

  internal static class HbrIfc4Add2Tc1SchemaProvenance
  {
    internal const string Repository = "buildingSMART/IFC4.x-development";
    internal const string Commit =
      "119bf71c8049cd0683df0109844605e975025db2";
    internal const string SchemaPath =
      "reference_schemas/IFC4_ADD2_TC1.exp";
    internal const string GitBlobSha1 =
      "cc49e47a9457bf8708a0db75c76308c19f0bd09b";
    internal const string SchemaSha256 =
      "a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046";
  }

  internal static class HbrIfcRootCarrierTypes
  {
    // buildingSMART/IFC4.x-development, fixed commit
    // 119bf71c8049cd0683df0109844605e975025db2,
    // reference_schemas/IFC4_ADD2_TC1.exp, Git blob
    // cc49e47a9457bf8708a0db75c76308c19f0bd09b, schema SHA-256
    // a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046.
    // This is the explicit 419-ENTITY inheritance closure whose SUBTYPE chain
    // reaches IfcRoot; never infer GlobalId ownership from arg0.
    private const string Ifc4Add2Tc1Names =
    "IFCACTIONREQUEST|IFCACTOR|IFCACTUATOR|IFCACTUATORTYPE|IFCAIRTERMINAL|IFCAIRTERMINALBOX|IFCAIRTERMINA"
      + "LBOXTYPE|IFCAIRTERMINALTYPE|IFCAIRTOAIRHEATRECOVERY|IFCAIRTOAIRHEATRECOVERYTYPE|IFCALARM|IFCALARMTYP"
      + "E|IFCANNOTATION|IFCASSET|IFCAUDIOVISUALAPPLIANCE|IFCAUDIOVISUALAPPLIANCETYPE|IFCBEAM|IFCBEAMSTANDARD"
      + "CASE|IFCBEAMTYPE|IFCBOILER|IFCBOILERTYPE|IFCBUILDING|IFCBUILDINGELEMENT|IFCBUILDINGELEMENTPART|IFCBU"
      + "ILDINGELEMENTPARTTYPE|IFCBUILDINGELEMENTPROXY|IFCBUILDINGELEMENTPROXYTYPE|IFCBUILDINGELEMENTTYPE|IFC"
      + "BUILDINGSTOREY|IFCBUILDINGSYSTEM|IFCBURNER|IFCBURNERTYPE|IFCCABLECARRIERFITTING|IFCCABLECARRIERFITTI"
      + "NGTYPE|IFCCABLECARRIERSEGMENT|IFCCABLECARRIERSEGMENTTYPE|IFCCABLEFITTING|IFCCABLEFITTINGTYPE|IFCCABL"
      + "ESEGMENT|IFCCABLESEGMENTTYPE|IFCCHILLER|IFCCHILLERTYPE|IFCCHIMNEY|IFCCHIMNEYTYPE|IFCCIVILELEMENT|IFC"
      + "CIVILELEMENTTYPE|IFCCOIL|IFCCOILTYPE|IFCCOLUMN|IFCCOLUMNSTANDARDCASE|IFCCOLUMNTYPE|IFCCOMMUNICATIONS"
      + "APPLIANCE|IFCCOMMUNICATIONSAPPLIANCETYPE|IFCCOMPLEXPROPERTYTEMPLATE|IFCCOMPRESSOR|IFCCOMPRESSORTYPE|"
      + "IFCCONDENSER|IFCCONDENSERTYPE|IFCCONSTRUCTIONEQUIPMENTRESOURCE|IFCCONSTRUCTIONEQUIPMENTRESOURCETYPE|"
      + "IFCCONSTRUCTIONMATERIALRESOURCE|IFCCONSTRUCTIONMATERIALRESOURCETYPE|IFCCONSTRUCTIONPRODUCTRESOURCE|I"
      + "FCCONSTRUCTIONPRODUCTRESOURCETYPE|IFCCONSTRUCTIONRESOURCE|IFCCONSTRUCTIONRESOURCETYPE|IFCCONTEXT|IFC"
      + "CONTROL|IFCCONTROLLER|IFCCONTROLLERTYPE|IFCCOOLEDBEAM|IFCCOOLEDBEAMTYPE|IFCCOOLINGTOWER|IFCCOOLINGTO"
      + "WERTYPE|IFCCOSTITEM|IFCCOSTSCHEDULE|IFCCOVERING|IFCCOVERINGTYPE|IFCCREWRESOURCE|IFCCREWRESOURCETYPE|"
      + "IFCCURTAINWALL|IFCCURTAINWALLTYPE|IFCDAMPER|IFCDAMPERTYPE|IFCDISCRETEACCESSORY|IFCDISCRETEACCESSORYT"
      + "YPE|IFCDISTRIBUTIONCHAMBERELEMENT|IFCDISTRIBUTIONCHAMBERELEMENTTYPE|IFCDISTRIBUTIONCIRCUIT|IFCDISTRI"
      + "BUTIONCONTROLELEMENT|IFCDISTRIBUTIONCONTROLELEMENTTYPE|IFCDISTRIBUTIONELEMENT|IFCDISTRIBUTIONELEMENT"
      + "TYPE|IFCDISTRIBUTIONFLOWELEMENT|IFCDISTRIBUTIONFLOWELEMENTTYPE|IFCDISTRIBUTIONPORT|IFCDISTRIBUTIONSY"
      + "STEM|IFCDOOR|IFCDOORLININGPROPERTIES|IFCDOORPANELPROPERTIES|IFCDOORSTANDARDCASE|IFCDOORSTYLE|IFCDOOR"
      + "TYPE|IFCDUCTFITTING|IFCDUCTFITTINGTYPE|IFCDUCTSEGMENT|IFCDUCTSEGMENTTYPE|IFCDUCTSILENCER|IFCDUCTSILE"
      + "NCERTYPE|IFCELECTRICAPPLIANCE|IFCELECTRICAPPLIANCETYPE|IFCELECTRICDISTRIBUTIONBOARD|IFCELECTRICDISTR"
      + "IBUTIONBOARDTYPE|IFCELECTRICFLOWSTORAGEDEVICE|IFCELECTRICFLOWSTORAGEDEVICETYPE|IFCELECTRICGENERATOR|"
      + "IFCELECTRICGENERATORTYPE|IFCELECTRICMOTOR|IFCELECTRICMOTORTYPE|IFCELECTRICTIMECONTROL|IFCELECTRICTIM"
      + "ECONTROLTYPE|IFCELEMENT|IFCELEMENTASSEMBLY|IFCELEMENTASSEMBLYTYPE|IFCELEMENTCOMPONENT|IFCELEMENTCOMP"
      + "ONENTTYPE|IFCELEMENTQUANTITY|IFCELEMENTTYPE|IFCENERGYCONVERSIONDEVICE|IFCENERGYCONVERSIONDEVICETYPE|"
      + "IFCENGINE|IFCENGINETYPE|IFCEVAPORATIVECOOLER|IFCEVAPORATIVECOOLERTYPE|IFCEVAPORATOR|IFCEVAPORATORTYP"
      + "E|IFCEVENT|IFCEVENTTYPE|IFCEXTERNALSPATIALELEMENT|IFCEXTERNALSPATIALSTRUCTUREELEMENT|IFCFAN|IFCFANTY"
      + "PE|IFCFASTENER|IFCFASTENERTYPE|IFCFEATUREELEMENT|IFCFEATUREELEMENTADDITION|IFCFEATUREELEMENTSUBTRACT"
      + "ION|IFCFILTER|IFCFILTERTYPE|IFCFIRESUPPRESSIONTERMINAL|IFCFIRESUPPRESSIONTERMINALTYPE|IFCFLOWCONTROL"
      + "LER|IFCFLOWCONTROLLERTYPE|IFCFLOWFITTING|IFCFLOWFITTINGTYPE|IFCFLOWINSTRUMENT|IFCFLOWINSTRUMENTTYPE|"
      + "IFCFLOWMETER|IFCFLOWMETERTYPE|IFCFLOWMOVINGDEVICE|IFCFLOWMOVINGDEVICETYPE|IFCFLOWSEGMENT|IFCFLOWSEGM"
      + "ENTTYPE|IFCFLOWSTORAGEDEVICE|IFCFLOWSTORAGEDEVICETYPE|IFCFLOWTERMINAL|IFCFLOWTERMINALTYPE|IFCFLOWTRE"
      + "ATMENTDEVICE|IFCFLOWTREATMENTDEVICETYPE|IFCFOOTING|IFCFOOTINGTYPE|IFCFURNISHINGELEMENT|IFCFURNISHING"
      + "ELEMENTTYPE|IFCFURNITURE|IFCFURNITURETYPE|IFCGEOGRAPHICELEMENT|IFCGEOGRAPHICELEMENTTYPE|IFCGRID|IFCG"
      + "ROUP|IFCHEATEXCHANGER|IFCHEATEXCHANGERTYPE|IFCHUMIDIFIER|IFCHUMIDIFIERTYPE|IFCINTERCEPTOR|IFCINTERCE"
      + "PTORTYPE|IFCINVENTORY|IFCJUNCTIONBOX|IFCJUNCTIONBOXTYPE|IFCLABORRESOURCE|IFCLABORRESOURCETYPE|IFCLAM"
      + "P|IFCLAMPTYPE|IFCLIGHTFIXTURE|IFCLIGHTFIXTURETYPE|IFCMECHANICALFASTENER|IFCMECHANICALFASTENERTYPE|IF"
      + "CMEDICALDEVICE|IFCMEDICALDEVICETYPE|IFCMEMBER|IFCMEMBERSTANDARDCASE|IFCMEMBERTYPE|IFCMOTORCONNECTION"
      + "|IFCMOTORCONNECTIONTYPE|IFCOBJECT|IFCOBJECTDEFINITION|IFCOCCUPANT|IFCOPENINGELEMENT|IFCOPENINGSTANDA"
      + "RDCASE|IFCOUTLET|IFCOUTLETTYPE|IFCPERFORMANCEHISTORY|IFCPERMEABLECOVERINGPROPERTIES|IFCPERMIT|IFCPIL"
      + "E|IFCPILETYPE|IFCPIPEFITTING|IFCPIPEFITTINGTYPE|IFCPIPESEGMENT|IFCPIPESEGMENTTYPE|IFCPLATE|IFCPLATES"
      + "TANDARDCASE|IFCPLATETYPE|IFCPORT|IFCPREDEFINEDPROPERTYSET|IFCPROCEDURE|IFCPROCEDURETYPE|IFCPROCESS|I"
      + "FCPRODUCT|IFCPROJECT|IFCPROJECTIONELEMENT|IFCPROJECTLIBRARY|IFCPROJECTORDER|IFCPROPERTYDEFINITION|IF"
      + "CPROPERTYSET|IFCPROPERTYSETDEFINITION|IFCPROPERTYSETTEMPLATE|IFCPROPERTYTEMPLATE|IFCPROPERTYTEMPLATE"
      + "DEFINITION|IFCPROTECTIVEDEVICE|IFCPROTECTIVEDEVICETRIPPINGUNIT|IFCPROTECTIVEDEVICETRIPPINGUNITTYPE|I"
      + "FCPROTECTIVEDEVICETYPE|IFCPROXY|IFCPUMP|IFCPUMPTYPE|IFCQUANTITYSET|IFCRAILING|IFCRAILINGTYPE|IFCRAMP"
      + "|IFCRAMPFLIGHT|IFCRAMPFLIGHTTYPE|IFCRAMPTYPE|IFCREINFORCEMENTDEFINITIONPROPERTIES|IFCREINFORCINGBAR|"
      + "IFCREINFORCINGBARTYPE|IFCREINFORCINGELEMENT|IFCREINFORCINGELEMENTTYPE|IFCREINFORCINGMESH|IFCREINFORC"
      + "INGMESHTYPE|IFCRELAGGREGATES|IFCRELASSIGNS|IFCRELASSIGNSTOACTOR|IFCRELASSIGNSTOCONTROL|IFCRELASSIGNS"
      + "TOGROUP|IFCRELASSIGNSTOGROUPBYFACTOR|IFCRELASSIGNSTOPROCESS|IFCRELASSIGNSTOPRODUCT|IFCRELASSIGNSTORE"
      + "SOURCE|IFCRELASSOCIATES|IFCRELASSOCIATESAPPROVAL|IFCRELASSOCIATESCLASSIFICATION|IFCRELASSOCIATESCONS"
      + "TRAINT|IFCRELASSOCIATESDOCUMENT|IFCRELASSOCIATESLIBRARY|IFCRELASSOCIATESMATERIAL|IFCRELATIONSHIP|IFC"
      + "RELCONNECTS|IFCRELCONNECTSELEMENTS|IFCRELCONNECTSPATHELEMENTS|IFCRELCONNECTSPORTS|IFCRELCONNECTSPORT"
      + "TOELEMENT|IFCRELCONNECTSSTRUCTURALACTIVITY|IFCRELCONNECTSSTRUCTURALMEMBER|IFCRELCONNECTSWITHECCENTRI"
      + "CITY|IFCRELCONNECTSWITHREALIZINGELEMENTS|IFCRELCONTAINEDINSPATIALSTRUCTURE|IFCRELCOVERSBLDGELEMENTS|"
      + "IFCRELCOVERSSPACES|IFCRELDECLARES|IFCRELDECOMPOSES|IFCRELDEFINES|IFCRELDEFINESBYOBJECT|IFCRELDEFINES"
      + "BYPROPERTIES|IFCRELDEFINESBYTEMPLATE|IFCRELDEFINESBYTYPE|IFCRELFILLSELEMENT|IFCRELFLOWCONTROLELEMENT"
      + "S|IFCRELINTERFERESELEMENTS|IFCRELNESTS|IFCRELPROJECTSELEMENT|IFCRELREFERENCEDINSPATIALSTRUCTURE|IFCR"
      + "ELSEQUENCE|IFCRELSERVICESBUILDINGS|IFCRELSPACEBOUNDARY|IFCRELSPACEBOUNDARY1STLEVEL|IFCRELSPACEBOUNDA"
      + "RY2NDLEVEL|IFCRELVOIDSELEMENT|IFCRESOURCE|IFCROOF|IFCROOFTYPE|IFCROOT|IFCSANITARYTERMINAL|IFCSANITAR"
      + "YTERMINALTYPE|IFCSENSOR|IFCSENSORTYPE|IFCSHADINGDEVICE|IFCSHADINGDEVICETYPE|IFCSIMPLEPROPERTYTEMPLAT"
      + "E|IFCSITE|IFCSLAB|IFCSLABELEMENTEDCASE|IFCSLABSTANDARDCASE|IFCSLABTYPE|IFCSOLARDEVICE|IFCSOLARDEVICE"
      + "TYPE|IFCSPACE|IFCSPACEHEATER|IFCSPACEHEATERTYPE|IFCSPACETYPE|IFCSPATIALELEMENT|IFCSPATIALELEMENTTYPE"
      + "|IFCSPATIALSTRUCTUREELEMENT|IFCSPATIALSTRUCTUREELEMENTTYPE|IFCSPATIALZONE|IFCSPATIALZONETYPE|IFCSTAC"
      + "KTERMINAL|IFCSTACKTERMINALTYPE|IFCSTAIR|IFCSTAIRFLIGHT|IFCSTAIRFLIGHTTYPE|IFCSTAIRTYPE|IFCSTRUCTURAL"
      + "ACTION|IFCSTRUCTURALACTIVITY|IFCSTRUCTURALANALYSISMODEL|IFCSTRUCTURALCONNECTION|IFCSTRUCTURALCURVEAC"
      + "TION|IFCSTRUCTURALCURVECONNECTION|IFCSTRUCTURALCURVEMEMBER|IFCSTRUCTURALCURVEMEMBERVARYING|IFCSTRUCT"
      + "URALCURVEREACTION|IFCSTRUCTURALITEM|IFCSTRUCTURALLINEARACTION|IFCSTRUCTURALLOADCASE|IFCSTRUCTURALLOA"
      + "DGROUP|IFCSTRUCTURALMEMBER|IFCSTRUCTURALPLANARACTION|IFCSTRUCTURALPOINTACTION|IFCSTRUCTURALPOINTCONN"
      + "ECTION|IFCSTRUCTURALPOINTREACTION|IFCSTRUCTURALREACTION|IFCSTRUCTURALRESULTGROUP|IFCSTRUCTURALSURFAC"
      + "EACTION|IFCSTRUCTURALSURFACECONNECTION|IFCSTRUCTURALSURFACEMEMBER|IFCSTRUCTURALSURFACEMEMBERVARYING|"
      + "IFCSTRUCTURALSURFACEREACTION|IFCSUBCONTRACTRESOURCE|IFCSUBCONTRACTRESOURCETYPE|IFCSURFACEFEATURE|IFC"
      + "SWITCHINGDEVICE|IFCSWITCHINGDEVICETYPE|IFCSYSTEM|IFCSYSTEMFURNITUREELEMENT|IFCSYSTEMFURNITUREELEMENT"
      + "TYPE|IFCTANK|IFCTANKTYPE|IFCTASK|IFCTASKTYPE|IFCTENDON|IFCTENDONANCHOR|IFCTENDONANCHORTYPE|IFCTENDON"
      + "TYPE|IFCTRANSFORMER|IFCTRANSFORMERTYPE|IFCTRANSPORTELEMENT|IFCTRANSPORTELEMENTTYPE|IFCTUBEBUNDLE|IFC"
      + "TUBEBUNDLETYPE|IFCTYPEOBJECT|IFCTYPEPROCESS|IFCTYPEPRODUCT|IFCTYPERESOURCE|IFCUNITARYCONTROLELEMENT|"
      + "IFCUNITARYCONTROLELEMENTTYPE|IFCUNITARYEQUIPMENT|IFCUNITARYEQUIPMENTTYPE|IFCVALVE|IFCVALVETYPE|IFCVI"
      + "BRATIONISOLATOR|IFCVIBRATIONISOLATORTYPE|IFCVIRTUALELEMENT|IFCVOIDINGFEATURE|IFCWALL|IFCWALLELEMENTE"
      + "DCASE|IFCWALLSTANDARDCASE|IFCWALLTYPE|IFCWASTETERMINAL|IFCWASTETERMINALTYPE|IFCWINDOW|IFCWINDOWLININ"
      + "GPROPERTIES|IFCWINDOWPANELPROPERTIES|IFCWINDOWSTANDARDCASE|IFCWINDOWSTYLE|IFCWINDOWTYPE|IFCWORKCALEN"
      + "DAR|IFCWORKCONTROL|IFCWORKPLAN|IFCWORKSCHEDULE|IFCZONE";

    private static readonly HashSet<string> Names = new HashSet<string>(
      Ifc4Add2Tc1Names.Split('|'),
      StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string entityType)
    {
      return entityType != null && Names.Contains(entityType);
    }
  }

  internal static class HbrIfcRelatedObjectTypes
  {
    // buildingSMART/IFC4.x-development, fixed commit
    // 119bf71c8049cd0683df0109844605e975025db2,
    // reference_schemas/IFC4_ADD2_TC1.exp, Git blob
    // cc49e47a9457bf8708a0db75c76308c19f0bd09b, schema SHA-256
    // a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046.
    // Explicit 215-ENTITY set: IfcObjectDefinition inheritance closure minus
    // the IfcTypeObject closure, matching IfcRelDefinesByProperties.RelatedObjects.
    private const string Ifc4Add2Tc1Names =
      "IFCACTIONREQUEST|IFCACTOR|IFCACTUATOR|IFCAIRTERMINAL|IFCAIRTERMINALBOX|IFCAIRTOAIRHEATRECOVERY|IFCAL"
      + "ARM|IFCANNOTATION|IFCASSET|IFCAUDIOVISUALAPPLIANCE|IFCBEAM|IFCBEAMSTANDARDCASE|IFCBOILER|IFCBUILDING"
      + "|IFCBUILDINGELEMENT|IFCBUILDINGELEMENTPART|IFCBUILDINGELEMENTPROXY|IFCBUILDINGSTOREY|IFCBUILDINGSYST"
      + "EM|IFCBURNER|IFCCABLECARRIERFITTING|IFCCABLECARRIERSEGMENT|IFCCABLEFITTING|IFCCABLESEGMENT|IFCCHILLE"
      + "R|IFCCHIMNEY|IFCCIVILELEMENT|IFCCOIL|IFCCOLUMN|IFCCOLUMNSTANDARDCASE|IFCCOMMUNICATIONSAPPLIANCE|IFCC"
      + "OMPRESSOR|IFCCONDENSER|IFCCONSTRUCTIONEQUIPMENTRESOURCE|IFCCONSTRUCTIONMATERIALRESOURCE|IFCCONSTRUCT"
      + "IONPRODUCTRESOURCE|IFCCONSTRUCTIONRESOURCE|IFCCONTEXT|IFCCONTROL|IFCCONTROLLER|IFCCOOLEDBEAM|IFCCOOL"
      + "INGTOWER|IFCCOSTITEM|IFCCOSTSCHEDULE|IFCCOVERING|IFCCREWRESOURCE|IFCCURTAINWALL|IFCDAMPER|IFCDISCRET"
      + "EACCESSORY|IFCDISTRIBUTIONCHAMBERELEMENT|IFCDISTRIBUTIONCIRCUIT|IFCDISTRIBUTIONCONTROLELEMENT|IFCDIS"
      + "TRIBUTIONELEMENT|IFCDISTRIBUTIONFLOWELEMENT|IFCDISTRIBUTIONPORT|IFCDISTRIBUTIONSYSTEM|IFCDOOR|IFCDOO"
      + "RSTANDARDCASE|IFCDUCTFITTING|IFCDUCTSEGMENT|IFCDUCTSILENCER|IFCELECTRICAPPLIANCE|IFCELECTRICDISTRIBU"
      + "TIONBOARD|IFCELECTRICFLOWSTORAGEDEVICE|IFCELECTRICGENERATOR|IFCELECTRICMOTOR|IFCELECTRICTIMECONTROL|"
      + "IFCELEMENT|IFCELEMENTASSEMBLY|IFCELEMENTCOMPONENT|IFCENERGYCONVERSIONDEVICE|IFCENGINE|IFCEVAPORATIVE"
      + "COOLER|IFCEVAPORATOR|IFCEVENT|IFCEXTERNALSPATIALELEMENT|IFCEXTERNALSPATIALSTRUCTUREELEMENT|IFCFAN|IF"
      + "CFASTENER|IFCFEATUREELEMENT|IFCFEATUREELEMENTADDITION|IFCFEATUREELEMENTSUBTRACTION|IFCFILTER|IFCFIRE"
      + "SUPPRESSIONTERMINAL|IFCFLOWCONTROLLER|IFCFLOWFITTING|IFCFLOWINSTRUMENT|IFCFLOWMETER|IFCFLOWMOVINGDEV"
      + "ICE|IFCFLOWSEGMENT|IFCFLOWSTORAGEDEVICE|IFCFLOWTERMINAL|IFCFLOWTREATMENTDEVICE|IFCFOOTING|IFCFURNISH"
      + "INGELEMENT|IFCFURNITURE|IFCGEOGRAPHICELEMENT|IFCGRID|IFCGROUP|IFCHEATEXCHANGER|IFCHUMIDIFIER|IFCINTE"
      + "RCEPTOR|IFCINVENTORY|IFCJUNCTIONBOX|IFCLABORRESOURCE|IFCLAMP|IFCLIGHTFIXTURE|IFCMECHANICALFASTENER|I"
      + "FCMEDICALDEVICE|IFCMEMBER|IFCMEMBERSTANDARDCASE|IFCMOTORCONNECTION|IFCOBJECT|IFCOBJECTDEFINITION|IFC"
      + "OCCUPANT|IFCOPENINGELEMENT|IFCOPENINGSTANDARDCASE|IFCOUTLET|IFCPERFORMANCEHISTORY|IFCPERMIT|IFCPILE|"
      + "IFCPIPEFITTING|IFCPIPESEGMENT|IFCPLATE|IFCPLATESTANDARDCASE|IFCPORT|IFCPROCEDURE|IFCPROCESS|IFCPRODU"
      + "CT|IFCPROJECT|IFCPROJECTIONELEMENT|IFCPROJECTLIBRARY|IFCPROJECTORDER|IFCPROTECTIVEDEVICE|IFCPROTECTI"
      + "VEDEVICETRIPPINGUNIT|IFCPROXY|IFCPUMP|IFCRAILING|IFCRAMP|IFCRAMPFLIGHT|IFCREINFORCINGBAR|IFCREINFORC"
      + "INGELEMENT|IFCREINFORCINGMESH|IFCRESOURCE|IFCROOF|IFCSANITARYTERMINAL|IFCSENSOR|IFCSHADINGDEVICE|IFC"
      + "SITE|IFCSLAB|IFCSLABELEMENTEDCASE|IFCSLABSTANDARDCASE|IFCSOLARDEVICE|IFCSPACE|IFCSPACEHEATER|IFCSPAT"
      + "IALELEMENT|IFCSPATIALSTRUCTUREELEMENT|IFCSPATIALZONE|IFCSTACKTERMINAL|IFCSTAIR|IFCSTAIRFLIGHT|IFCSTR"
      + "UCTURALACTION|IFCSTRUCTURALACTIVITY|IFCSTRUCTURALANALYSISMODEL|IFCSTRUCTURALCONNECTION|IFCSTRUCTURAL"
      + "CURVEACTION|IFCSTRUCTURALCURVECONNECTION|IFCSTRUCTURALCURVEMEMBER|IFCSTRUCTURALCURVEMEMBERVARYING|IF"
      + "CSTRUCTURALCURVEREACTION|IFCSTRUCTURALITEM|IFCSTRUCTURALLINEARACTION|IFCSTRUCTURALLOADCASE|IFCSTRUCT"
      + "URALLOADGROUP|IFCSTRUCTURALMEMBER|IFCSTRUCTURALPLANARACTION|IFCSTRUCTURALPOINTACTION|IFCSTRUCTURALPO"
      + "INTCONNECTION|IFCSTRUCTURALPOINTREACTION|IFCSTRUCTURALREACTION|IFCSTRUCTURALRESULTGROUP|IFCSTRUCTURA"
      + "LSURFACEACTION|IFCSTRUCTURALSURFACECONNECTION|IFCSTRUCTURALSURFACEMEMBER|IFCSTRUCTURALSURFACEMEMBERV"
      + "ARYING|IFCSTRUCTURALSURFACEREACTION|IFCSUBCONTRACTRESOURCE|IFCSURFACEFEATURE|IFCSWITCHINGDEVICE|IFCS"
      + "YSTEM|IFCSYSTEMFURNITUREELEMENT|IFCTANK|IFCTASK|IFCTENDON|IFCTENDONANCHOR|IFCTRANSFORMER|IFCTRANSPOR"
      + "TELEMENT|IFCTUBEBUNDLE|IFCUNITARYCONTROLELEMENT|IFCUNITARYEQUIPMENT|IFCVALVE|IFCVIBRATIONISOLATOR|IF"
      + "CVIRTUALELEMENT|IFCVOIDINGFEATURE|IFCWALL|IFCWALLELEMENTEDCASE|IFCWALLSTANDARDCASE|IFCWASTETERMINAL|"
      + "IFCWINDOW|IFCWINDOWSTANDARDCASE|IFCWORKCALENDAR|IFCWORKCONTROL|IFCWORKPLAN|IFCWORKSCHEDULE|IFCZONE";

    private static readonly HashSet<string> Names = new HashSet<string>(
      Ifc4Add2Tc1Names.Split('|'),
      StringComparer.OrdinalIgnoreCase);

    private const string AbstractIfc4Add2Tc1Names =
      "IFCBUILDINGELEMENT|IFCCONSTRUCTIONRESOURCE|IFCCONTEXT|IFCCONTROL|IFCELEMENT|IFCELEMENTCOMPONENT|"
      + "IFCEXTERNALSPATIALSTRUCTUREELEMENT|IFCFEATUREELEMENT|IFCFEATUREELEMENTADDITION|"
      + "IFCFEATUREELEMENTSUBTRACTION|IFCOBJECT|IFCOBJECTDEFINITION|IFCPORT|IFCPROCESS|IFCPRODUCT|"
      + "IFCREINFORCINGELEMENT|IFCRESOURCE|IFCSPATIALELEMENT|IFCSPATIALSTRUCTUREELEMENT|"
      + "IFCSTRUCTURALACTION|IFCSTRUCTURALACTIVITY|IFCSTRUCTURALCONNECTION|IFCSTRUCTURALITEM|"
      + "IFCSTRUCTURALMEMBER|IFCSTRUCTURALREACTION|IFCWORKCONTROL";

    private static readonly HashSet<string> AbstractNames =
      new HashSet<string>(
        AbstractIfc4Add2Tc1Names.Split('|'),
        StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string entityType)
    {
      return entityType != null
        && Names.Contains(entityType.Trim())
        && !AbstractNames.Contains(entityType.Trim());
    }
  }

  internal static class HbrIfcTypeObjectTypes
  {
    // buildingSMART/IFC4.x-development, fixed commit
    // 119bf71c8049cd0683df0109844605e975025db2,
    // reference_schemas/IFC4_ADD2_TC1.exp, Git blob
    // cc49e47a9457bf8708a0db75c76308c19f0bd09b, schema SHA-256
    // a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046.
    // Full 138-ENTITY IfcTypeObject inheritance closure. Runtime instances
    // additionally exclude the 19 EXPRESS ABSTRACT entities below.
    private const string Ifc4Add2Tc1Names =
      "IFCACTUATORTYPE|IFCAIRTERMINALBOXTYPE|IFCAIRTERMINALTYPE|IFCAIRTOAIRHEATRECOVERYTYPE|IFCALARMTYPE|"
      + "IFCAUDIOVISUALAPPLIANCETYPE|IFCBEAMTYPE|IFCBOILERTYPE|IFCBUILDINGELEMENTPARTTYPE|"
      + "IFCBUILDINGELEMENTPROXYTYPE|IFCBUILDINGELEMENTTYPE|IFCBURNERTYPE|IFCCABLECARRIERFITTINGTYPE|"
      + "IFCCABLECARRIERSEGMENTTYPE|IFCCABLEFITTINGTYPE|IFCCABLESEGMENTTYPE|IFCCHILLERTYPE|IFCCHIMNEYTYPE|"
      + "IFCCIVILELEMENTTYPE|IFCCOILTYPE|IFCCOLUMNTYPE|IFCCOMMUNICATIONSAPPLIANCETYPE|IFCCOMPRESSORTYPE|"
      + "IFCCONDENSERTYPE|IFCCONSTRUCTIONEQUIPMENTRESOURCETYPE|IFCCONSTRUCTIONMATERIALRESOURCETYPE|"
      + "IFCCONSTRUCTIONPRODUCTRESOURCETYPE|IFCCONSTRUCTIONRESOURCETYPE|IFCCONTROLLERTYPE|IFCCOOLEDBEAMTYPE|"
      + "IFCCOOLINGTOWERTYPE|IFCCOVERINGTYPE|IFCCREWRESOURCETYPE|IFCCURTAINWALLTYPE|IFCDAMPERTYPE|"
      + "IFCDISCRETEACCESSORYTYPE|IFCDISTRIBUTIONCHAMBERELEMENTTYPE|IFCDISTRIBUTIONCONTROLELEMENTTYPE|"
      + "IFCDISTRIBUTIONELEMENTTYPE|IFCDISTRIBUTIONFLOWELEMENTTYPE|IFCDOORSTYLE|IFCDOORTYPE|"
      + "IFCDUCTFITTINGTYPE|IFCDUCTSEGMENTTYPE|IFCDUCTSILENCERTYPE|IFCELECTRICAPPLIANCETYPE|"
      + "IFCELECTRICDISTRIBUTIONBOARDTYPE|IFCELECTRICFLOWSTORAGEDEVICETYPE|IFCELECTRICGENERATORTYPE|"
      + "IFCELECTRICMOTORTYPE|IFCELECTRICTIMECONTROLTYPE|IFCELEMENTASSEMBLYTYPE|IFCELEMENTCOMPONENTTYPE|"
      + "IFCELEMENTTYPE|IFCENERGYCONVERSIONDEVICETYPE|IFCENGINETYPE|IFCEVAPORATIVECOOLERTYPE|"
      + "IFCEVAPORATORTYPE|IFCEVENTTYPE|IFCFANTYPE|IFCFASTENERTYPE|IFCFILTERTYPE|"
      + "IFCFIRESUPPRESSIONTERMINALTYPE|IFCFLOWCONTROLLERTYPE|IFCFLOWFITTINGTYPE|IFCFLOWINSTRUMENTTYPE|"
      + "IFCFLOWMETERTYPE|IFCFLOWMOVINGDEVICETYPE|IFCFLOWSEGMENTTYPE|IFCFLOWSTORAGEDEVICETYPE|"
      + "IFCFLOWTERMINALTYPE|IFCFLOWTREATMENTDEVICETYPE|IFCFOOTINGTYPE|IFCFURNISHINGELEMENTTYPE|"
      + "IFCFURNITURETYPE|IFCGEOGRAPHICELEMENTTYPE|IFCHEATEXCHANGERTYPE|IFCHUMIDIFIERTYPE|"
      + "IFCINTERCEPTORTYPE|IFCJUNCTIONBOXTYPE|IFCLABORRESOURCETYPE|IFCLAMPTYPE|IFCLIGHTFIXTURETYPE|"
      + "IFCMECHANICALFASTENERTYPE|IFCMEDICALDEVICETYPE|IFCMEMBERTYPE|IFCMOTORCONNECTIONTYPE|IFCOUTLETTYPE|"
      + "IFCPILETYPE|IFCPIPEFITTINGTYPE|IFCPIPESEGMENTTYPE|IFCPLATETYPE|IFCPROCEDURETYPE|"
      + "IFCPROTECTIVEDEVICETRIPPINGUNITTYPE|IFCPROTECTIVEDEVICETYPE|IFCPUMPTYPE|IFCRAILINGTYPE|"
      + "IFCRAMPFLIGHTTYPE|IFCRAMPTYPE|IFCREINFORCINGBARTYPE|IFCREINFORCINGELEMENTTYPE|"
      + "IFCREINFORCINGMESHTYPE|IFCROOFTYPE|IFCSANITARYTERMINALTYPE|IFCSENSORTYPE|IFCSHADINGDEVICETYPE|"
      + "IFCSLABTYPE|IFCSOLARDEVICETYPE|IFCSPACEHEATERTYPE|IFCSPACETYPE|IFCSPATIALELEMENTTYPE|"
      + "IFCSPATIALSTRUCTUREELEMENTTYPE|IFCSPATIALZONETYPE|IFCSTACKTERMINALTYPE|IFCSTAIRFLIGHTTYPE|"
      + "IFCSTAIRTYPE|IFCSUBCONTRACTRESOURCETYPE|IFCSWITCHINGDEVICETYPE|IFCSYSTEMFURNITUREELEMENTTYPE|"
      + "IFCTANKTYPE|IFCTASKTYPE|IFCTENDONANCHORTYPE|IFCTENDONTYPE|IFCTRANSFORMERTYPE|"
      + "IFCTRANSPORTELEMENTTYPE|IFCTUBEBUNDLETYPE|IFCTYPEOBJECT|IFCTYPEPROCESS|IFCTYPEPRODUCT|"
      + "IFCTYPERESOURCE|IFCUNITARYCONTROLELEMENTTYPE|IFCUNITARYEQUIPMENTTYPE|IFCVALVETYPE|"
      + "IFCVIBRATIONISOLATORTYPE|IFCWALLTYPE|IFCWASTETERMINALTYPE|IFCWINDOWSTYLE|IFCWINDOWTYPE";

    private const string AbstractIfc4Add2Tc1Names =
      "IFCBUILDINGELEMENTTYPE|IFCCONSTRUCTIONRESOURCETYPE|IFCDISTRIBUTIONCONTROLELEMENTTYPE|"
      + "IFCDISTRIBUTIONFLOWELEMENTTYPE|IFCELEMENTCOMPONENTTYPE|IFCELEMENTTYPE|"
      + "IFCENERGYCONVERSIONDEVICETYPE|IFCFLOWCONTROLLERTYPE|IFCFLOWFITTINGTYPE|"
      + "IFCFLOWMOVINGDEVICETYPE|IFCFLOWSEGMENTTYPE|IFCFLOWSTORAGEDEVICETYPE|IFCFLOWTERMINALTYPE|"
      + "IFCFLOWTREATMENTDEVICETYPE|IFCREINFORCINGELEMENTTYPE|IFCSPATIALELEMENTTYPE|"
      + "IFCSPATIALSTRUCTUREELEMENTTYPE|IFCTYPEPROCESS|IFCTYPERESOURCE";

    private static readonly HashSet<string> Names = new HashSet<string>(
      Ifc4Add2Tc1Names.Split('|'),
      StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AbstractNames =
      new HashSet<string>(
        AbstractIfc4Add2Tc1Names.Split('|'),
        StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string entityType)
    {
      return entityType != null && Names.Contains(entityType.Trim());
    }

    public static bool ContainsConcrete(string entityType)
    {
      return Contains(entityType) && !AbstractNames.Contains(entityType.Trim());
    }
  }

  internal static class HbrIfcPropertySetDefinitionTypes
  {
    // buildingSMART/IFC4.x-development, fixed commit
    // 119bf71c8049cd0683df0109844605e975025db2,
    // reference_schemas/IFC4_ADD2_TC1.exp, Git blob
    // cc49e47a9457bf8708a0db75c76308c19f0bd09b, schema SHA-256
    // a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046.
    // Full 11-ENTITY IfcPropertySetDefinition inheritance closure. The
    // separate concrete set prevents accepting EXPRESS ABSTRACT entities as
    // runtime instances.
    private static readonly HashSet<string> Names = new HashSet<string>(
      new[]
      {
        "IFCDOORLININGPROPERTIES",
        "IFCDOORPANELPROPERTIES",
        "IFCELEMENTQUANTITY",
        "IFCPERMEABLECOVERINGPROPERTIES",
        "IFCPREDEFINEDPROPERTYSET",
        "IFCPROPERTYSET",
        "IFCPROPERTYSETDEFINITION",
        "IFCQUANTITYSET",
        "IFCREINFORCEMENTDEFINITIONPROPERTIES",
        "IFCWINDOWLININGPROPERTIES",
        "IFCWINDOWPANELPROPERTIES"
      },
      StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ConcreteNames =
      new HashSet<string>(
        new[]
        {
          "IFCDOORLININGPROPERTIES",
          "IFCDOORPANELPROPERTIES",
          "IFCELEMENTQUANTITY",
          "IFCPERMEABLECOVERINGPROPERTIES",
          "IFCPROPERTYSET",
          "IFCREINFORCEMENTDEFINITIONPROPERTIES",
          "IFCWINDOWLININGPROPERTIES",
          "IFCWINDOWPANELPROPERTIES"
        },
        StringComparer.OrdinalIgnoreCase);

    public static bool ContainsConcrete(string entityType)
    {
      return entityType != null && ConcreteNames.Contains(entityType.Trim());
    }
  }

  internal static class HbrIfcPropertyTypes
  {
    // buildingSMART/IFC4.x-development, fixed commit
    // 119bf71c8049cd0683df0109844605e975025db2,
    // reference_schemas/IFC4_ADD2_TC1.exp, Git blob
    // cc49e47a9457bf8708a0db75c76308c19f0bd09b, schema SHA-256
    // a2704ba20a1b3d0b7d9b61d6fd37d0baa3b4996ba3e90d968a1d2ca2819d1046.
    // Full 9-ENTITY IfcProperty inheritance closure; IfcProperty and
    // IfcSimpleProperty are EXPRESS ABSTRACT and cannot be runtime instances.
    private static readonly HashSet<string> Names = new HashSet<string>(
      new[]
      {
        "IFCCOMPLEXPROPERTY",
        "IFCPROPERTY",
        "IFCPROPERTYBOUNDEDVALUE",
        "IFCPROPERTYENUMERATEDVALUE",
        "IFCPROPERTYLISTVALUE",
        "IFCPROPERTYREFERENCEVALUE",
        "IFCPROPERTYSINGLEVALUE",
        "IFCPROPERTYTABLEVALUE",
        "IFCSIMPLEPROPERTY"
      },
      StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AbstractNames =
      new HashSet<string>(
        new[] { "IFCPROPERTY", "IFCSIMPLEPROPERTY" },
        StringComparer.OrdinalIgnoreCase);

    public static bool ContainsConcrete(string entityType)
    {
      return entityType != null
        && Names.Contains(entityType.Trim())
        && !AbstractNames.Contains(entityType.Trim());
    }
  }

  internal static class HbrIfcPropertySetSemantics
  {
    public static bool TryResolveHasProperties(
      IfcStepDocument document,
      IfcStepEntity propertySet,
      out IReadOnlyList<int> propertyIds,
      out string errorCode,
      out string error)
    {
      propertyIds = null;
      errorCode = HbrIfcErrorCodes.IfcPropertySetConflict;
      error = null;
      if (document == null || propertySet == null)
      {
        error = "IfcPropertySet 校验输入不能为空。";
        return false;
      }
      if (!string.Equals(
        propertySet.Type,
        "IFCPROPERTYSET",
        StringComparison.OrdinalIgnoreCase)
        || propertySet.Arguments.Count != 5)
      {
        error = "IfcPropertySet 类型或参数数量无效：#" + propertySet.Id;
        return false;
      }

      IReadOnlyList<int> parsedIds;
      try
      {
        parsedIds = IfcStepSyntax.ParseReferenceList(
          propertySet.Arguments[4]);
      }
      catch (Exception exception)
      {
        error = "IfcPropertySet HasProperties 引用语法无效："
          + exception.Message;
        return false;
      }
      if (parsedIds.Count == 0)
      {
        error = "IfcPropertySet HasProperties 不能为空。";
        return false;
      }

      var uniqueIds = new HashSet<int>();
      foreach (int propertyId in parsedIds)
      {
        if (!uniqueIds.Add(propertyId))
        {
          errorCode = HbrIfcErrorCodes.IfcPropertyConflict;
          error = "IfcPropertySet HasProperties 包含重复引用。";
          return false;
        }
        if (!document.TryGetEntity(propertyId, out IfcStepEntity property))
        {
          error = "IfcPropertySet HasProperties 包含悬空引用。";
          return false;
        }
        if (!HbrIfcPropertyTypes.ContainsConcrete(property.Type))
        {
          error = "IfcPropertySet HasProperties 引用了不允许的实体类型：#"
            + property.Id + "=" + property.Type + "。";
          return false;
        }
      }
      propertyIds = parsedIds;
      return true;
    }
  }

  internal static class HbrIfcTypeObjectSemantics
  {
    public static bool TryResolveHasPropertySets(
      IfcStepDocument document,
      IfcStepEntity typeObject,
      out IReadOnlyList<int> definitionIds,
      out string error)
    {
      definitionIds = null;
      error = null;
      if (document == null || typeObject == null)
      {
        error = "IfcTypeObject 校验输入不能为空。";
        return false;
      }
      if (!HbrIfcTypeObjectTypes.ContainsConcrete(typeObject.Type))
      {
        error = "IfcTypeObject 使用了 ABSTRACT 或非 schema 实体类型：#"
          + typeObject.Id + "=" + typeObject.Type + "。";
        return false;
      }
      if (typeObject.Arguments.Count <= 5)
      {
        error = "IfcTypeObject 缺少 HasPropertySets 参数：#" + typeObject.Id;
        return false;
      }

      string token = typeObject.Arguments[5].Trim();
      if (string.Equals(token, "$", StringComparison.Ordinal))
      {
        definitionIds = Array.Empty<int>();
        return true;
      }

      IReadOnlyList<int> parsedIds;
      try
      {
        parsedIds = IfcStepSyntax.ParseReferenceList(token);
      }
      catch (Exception exception)
      {
        error = "IfcTypeObject HasPropertySets 引用语法无效："
          + exception.Message;
        return false;
      }
      if (parsedIds.Count == 0)
      {
        error = "IfcTypeObject HasPropertySets 不能为空集合。";
        return false;
      }
      var uniqueIds = new HashSet<int>();
      foreach (int definitionId in parsedIds)
      {
        if (!uniqueIds.Add(definitionId))
        {
          error = "IfcTypeObject HasPropertySets 包含重复引用。";
          return false;
        }
        if (!document.TryGetEntity(
          definitionId,
          out IfcStepEntity definition))
        {
          error = "IfcTypeObject HasPropertySets 包含悬空引用。";
          return false;
        }
        if (!HbrIfcPropertySetDefinitionTypes.ContainsConcrete(
          definition.Type))
        {
          error = "IfcTypeObject HasPropertySets 引用了不允许的实体类型：#"
            + definition.Id + "=" + definition.Type + "。";
          return false;
        }
      }
      definitionIds = parsedIds;
      return true;
    }
  }

  internal static class HbrIfcRelationshipSemantics
  {
    public static bool TryValidateRelatedObjects(
      IfcStepDocument document,
      IReadOnlyList<int> ownerIds,
      out string error)
    {
      error = null;
      if (ownerIds == null || ownerIds.Count == 0)
      {
        error = "IfcRelDefinesByProperties RelatedObjects 不能为空。";
        return false;
      }
      var uniqueIds = new HashSet<int>();
      foreach (int ownerId in ownerIds)
      {
        if (!uniqueIds.Add(ownerId))
        {
          error = "IfcRelDefinesByProperties RelatedObjects 包含重复引用。";
          return false;
        }
        if (!document.TryGetEntity(ownerId, out IfcStepEntity owner))
        {
          error = "IfcRelDefinesByProperties RelatedObjects 包含悬空引用。";
          return false;
        }
        if (!HbrIfcRelatedObjectTypes.Contains(owner.Type))
        {
          error = "IfcRelDefinesByProperties RelatedObjects 引用了不允许的实体类型：#"
            + owner.Id + "=" + owner.Type + "。";
          return false;
        }
      }
      return true;
    }

    public static bool TryResolvePropertySetDefinitions(
      IfcStepDocument document,
      string token,
      out IReadOnlyList<int> definitionIds,
      out string error)
    {
      definitionIds = null;
      error = null;
      if (document == null)
      {
        error = "IfcRelDefinesByProperties 缺少 IFC 文档。";
        return false;
      }

      IReadOnlyList<int> parsedIds;
      try
      {
        if (!string.IsNullOrWhiteSpace(token) && token.TrimStart()[0] == '#')
        {
          parsedIds = new[] { IfcStepSyntax.ParseReference(token.Trim()) };
        }
        else if (IfcStepSyntax.TryParseTypedValue(
          token,
          out string selectType,
          out string selectInner)
          && string.Equals(
            selectType,
            "IFCPROPERTYSETDEFINITIONSET",
            StringComparison.Ordinal))
        {
          parsedIds = IfcStepSyntax.ParseReferenceList(selectInner);
        }
        else
        {
          error = "IfcRelDefinesByProperties RelatingPropertyDefinition select 无效。";
          return false;
        }
      }
      catch (Exception exception)
      {
        error = "IfcRelDefinesByProperties RelatingPropertyDefinition 引用语法无效："
          + exception.Message;
        return false;
      }

      if (parsedIds.Count == 0)
      {
        error = "IfcPropertySetDefinitionSet 不能为空。";
        return false;
      }
      var uniqueIds = new HashSet<int>();
      foreach (int definitionId in parsedIds)
      {
        if (!uniqueIds.Add(definitionId))
        {
          error = "IfcPropertySetDefinitionSet 包含重复引用。";
          return false;
        }
        if (!document.TryGetEntity(
          definitionId,
          out IfcStepEntity definition))
        {
          error = "RelatingPropertyDefinition 包含悬空引用。";
          return false;
        }
        if (!HbrIfcPropertySetDefinitionTypes.ContainsConcrete(
          definition.Type))
        {
          error = "RelatingPropertyDefinition 引用了不允许的实体类型：#"
            + definition.Id + "=" + definition.Type + "。";
          return false;
        }
      }

      definitionIds = parsedIds;
      return true;
    }
  }


  internal static class HbrIfcErrorCodes
  {
    public const string IfcOwnerNotFound = "IFC_OWNER_NOT_FOUND";
    public const string IfcOwnerConflict = "IFC_OWNER_CONFLICT";
    public const string IfcPropertyConflict = "IFC_PROPERTY_CONFLICT";
    public const string IfcPropertySetConflict = "IFC_PROPERTY_SET_CONFLICT";
    public const string IfcRelationshipConflict = "IFC_RELATIONSHIP_CONFLICT";
    public const string InvalidValue = "INVALID_VALUE";
    public const string IfcFieldNotFound = "IFC_FIELD_NOT_FOUND";
    public const string IfcTypeMismatch = "IFC_TYPE_MISMATCH";
    public const string IfcValueMismatch = "IFC_VALUE_MISMATCH";
    public const string IfcMutationFailed = "IFC_MUTATION_FAILED";
    public const string RuleNotImplemented = "RULE_NOT_IMPLEMENTED";
    public const string TransactionAborted = "TRANSACTION_ABORTED";
  }

  internal sealed class HbrIfcEnrichmentValue
  {
    public string OwnerEntityType { get; set; }
    public string OwnerGlobalId { get; set; }
    public string OwnerStrategy { get; set; }
    public string PropertySetName { get; set; }
    public string PropertyName { get; set; }
    public string DeclaredIfcType { get; set; }
    public string CanonicalValue { get; set; }
    public string PropertyIdentity { get; set; }
    public string SemanticKey { get; set; }
  }

  internal sealed class HbrIfcEnrichmentFieldResult
  {
    public string PropertyIdentity { get; set; }
    public bool Success { get; set; }
    public string ErrorCode { get; set; }
    public string Message { get; set; }
    public bool ExactInspectionPassed { get; set; }
    public int? OwnerId { get; set; }
    public int? PropertyId { get; set; }
    public int? PropertySetId { get; set; }
    public int? RelationshipId { get; set; }
  }

  internal sealed class HbrIfcEnrichmentResult
  {
    public bool Success { get; set; }
    public int CreatedProperties { get; set; }
    public int CreatedPropertySets { get; set; }
    public int CreatedRelationships { get; set; }
    public int UpdatedProperties { get; set; }
    public IReadOnlyList<HbrIfcEnrichmentFieldResult> Fields { get; set; }
      = Array.Empty<HbrIfcEnrichmentFieldResult>();
  }

  internal sealed class HbrIfcFieldInspectionResult
  {
    public HbrIfcFieldInspectionResult(
      string propertyIdentity,
      bool success,
      string errorCode,
      string message,
      int? ownerId = null,
      int? propertyId = null,
      int? propertySetId = null,
      int? relationshipId = null,
      string actualIfcType = null,
      string typedToken = null)
    {
      if (errorCode == null) throw new ArgumentNullException(nameof(errorCode));
      if (message == null) throw new ArgumentNullException(nameof(message));
      if (success && errorCode.Length != 0)
        throw new ArgumentException(
          "成功的 IFC 字段检查不能携带错误码。",
          nameof(errorCode));
      if (!success && string.IsNullOrWhiteSpace(errorCode))
        throw new ArgumentException(
          "失败的 IFC 字段检查必须携带错误码。",
          nameof(errorCode));

      PropertyIdentity = propertyIdentity;
      Success = success;
      ErrorCode = errorCode;
      Message = message;
      OwnerId = ownerId;
      PropertyId = propertyId;
      PropertySetId = propertySetId;
      RelationshipId = relationshipId;
      ActualIfcType = actualIfcType;
      TypedToken = typedToken;
    }

    public string PropertyIdentity { get; }
    public bool Success { get; }
    public string ErrorCode { get; }
    public string Message { get; }
    public int? OwnerId { get; }
    public int? PropertyId { get; }
    public int? PropertySetId { get; }
    public int? RelationshipId { get; }
    public string ActualIfcType { get; }
    public string TypedToken { get; }
  }

  internal sealed class HbrIfcBatchInspectionResult
  {
    public HbrIfcBatchInspectionResult(
      bool success,
      string errorCode,
      string message,
      IReadOnlyList<HbrIfcFieldInspectionResult> fields)
    {
      if (errorCode == null) throw new ArgumentNullException(nameof(errorCode));
      if (message == null) throw new ArgumentNullException(nameof(message));
      if (fields == null) throw new ArgumentNullException(nameof(fields));

      var snapshot = new HbrIfcFieldInspectionResult[fields.Count];
      bool allFieldsSuccessful = true;
      for (int index = 0; index < fields.Count; index++)
      {
        HbrIfcFieldInspectionResult field = fields[index];
        if (field == null)
          throw new ArgumentNullException(
            nameof(fields),
            "IFC 批量检查字段不能包含 null。");
        snapshot[index] = field;
        if (!field.Success) allFieldsSuccessful = false;
      }

      if (success && errorCode.Length != 0)
        throw new ArgumentException(
          "成功的 IFC 批量检查不能携带错误码。",
          nameof(errorCode));
      if (!success && string.IsNullOrWhiteSpace(errorCode))
        throw new ArgumentException(
          "失败的 IFC 批量检查必须携带错误码。",
          nameof(errorCode));
      if (success && !allFieldsSuccessful)
        throw new ArgumentException(
          "成功的 IFC 批量检查不能包含失败字段。",
          nameof(fields));
      if (!success && snapshot.Length != 0 && allFieldsSuccessful)
        throw new ArgumentException(
          "失败的 IFC 批量检查不能只包含成功字段。",
          nameof(fields));

      Success = success;
      ErrorCode = errorCode;
      Message = message;
      Fields = Array.AsReadOnly(snapshot);
    }

    public bool Success { get; }
    public string ErrorCode { get; }
    public string Message { get; }
    public IReadOnlyList<HbrIfcFieldInspectionResult> Fields { get; }
  }
}
