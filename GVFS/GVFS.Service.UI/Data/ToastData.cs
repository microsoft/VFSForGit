using System.Xml.Serialization;

namespace GVFS.Service.UI.Data
{
    [XmlRoot("toast")]
    public class ToastData
    {
        [XmlAttribute("launch")]
        public string Launch { get; set; }

        [XmlElement("visual")]
        public VisualData Visual { get; set; }

        [XmlElement("actions")]
        public ActionsData Actions { get; set; }

        [XmlElement("scenario")]
        public string Scenario { get; set; }
    }
}
