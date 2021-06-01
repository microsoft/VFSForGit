using System.Xml.Serialization;

namespace GVFS.Service.UI.Data
{
    public abstract class BindingItem
    {
        [XmlRoot("text")]
        public class TextData : BindingItem
        {
            public TextData()
            {
                // Required for serialization
            }

            public TextData(string value)
            {
                this.Value = value;
            }

            [XmlText]
            public string Value { get; set; }
        }

        [XmlRoot("image")]
        public class ImageData : BindingItem
        {
            [XmlAttribute("placement")]
            public string Placement { get; set; }

            [XmlAttribute("src")]
            public string Source { get; set; }

            [XmlAttribute("hint-crop")]
            public string HintCrop { get; set; }
        }
    }
}
