using System.Xml.Serialization;

namespace RGFS.Service.UI.Data
{
    public class ActionsData
    {
        [XmlAnyElement("actions")]
        public XmlList<ActionItem> Actions { get; set; }
    }
}
