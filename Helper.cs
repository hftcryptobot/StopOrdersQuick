using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace QuikTester
{
    public class Helper
    {
        public static string SettingsFile = "bots.xml";

        public static void SaveXml<T>(T serializableObject)
        {
            var serializer = new DataContractSerializer(typeof(T));
            var settings = new XmlWriterSettings()
            {
                Indent = true,
                IndentChars = "\t",
            };
            var writer = XmlWriter.Create(SettingsFile, settings);
            serializer.WriteObject(writer, serializableObject);
            writer.Close();
        }

        public static T? ReadXml<T>()
        {


            var fileStream = new FileStream(SettingsFile, FileMode.Open);
            var reader = XmlDictionaryReader.CreateTextReader(fileStream, new XmlDictionaryReaderQuotas());
            var serializer = new DataContractSerializer(typeof(T));
            T serializableObject = (T)serializer.ReadObject(reader, true);
            reader.Close();
            fileStream.Close();
            return serializableObject;
        }


    }
}
