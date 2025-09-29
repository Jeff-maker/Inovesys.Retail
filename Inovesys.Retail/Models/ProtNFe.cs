using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Inovesys.Retail.Models
{

    [XmlRoot("protNFe", Namespace = "http://www.portalfiscal.inf.br/nfe")]
    public class ProtNFe
    {
        [XmlAttribute("versao")]
        public string Versao { get; set; }

        [XmlElement("infProt")]
        public InfProt InfProt { get; set; }
    }

    public class InfProt
    {
        [XmlElement("tpAmb")]
        public int TpAmb { get; set; }

        [XmlElement("verAplic")]
        public string VerAplic { get; set; }

        [XmlElement("chNFe")]
        public string ChNFe { get; set; }

        [XmlElement("dhRecbto")]
        public DateTime DhRecbto { get; set; }

        [XmlElement("nProt")]
        public string NProt { get; set; }

        [XmlElement("digVal")]
        public string DigVal { get; set; }

        [XmlElement("cStat")]
        public int CStat { get; set; }

        [XmlElement("xMotivo")]
        public string XMotivo { get; set; }
    }

}

