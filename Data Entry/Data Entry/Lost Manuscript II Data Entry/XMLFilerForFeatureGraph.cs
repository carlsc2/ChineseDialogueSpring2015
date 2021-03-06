﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using Dialogue_Data_Entry;
using System.Windows.Forms;
using System.Security;

namespace Dialogue_Data_Entry
{

    class XMLFilerForFeatureGraph
    {
        public static XmlDocument docOld = new XmlDocument();

        public static string escapeInvalidXML(string s)
        {
            if (s == null)
            {
                return s;
            }
            return SecurityElement.Escape(s);
        }

        public static string unEscapeInvalidXML(string s)
        {
            if (s == null)
            {
                return s;
            }
            string str = s;
            str = str.Replace("&apos;", "'");
            str = str.Replace("&quot;", "\""); 
            str = str.Replace("&gt;", ">");
            str = str.Replace("&lt;", "<");
            str = str.Replace("&amp;", "&");
            return str;
        }

        public static bool writeFeatureGraph(FeatureGraph toWrite, string fileName)
        {
            try
            {
                StreamWriter writer = new StreamWriter(fileName);
                writer.WriteLine("<AIMind>");
                if (toWrite.Root != null)
                {
                    writer.WriteLine("<Root id=\"" + toWrite.Root.Id + "\"/>");
                }
                //Start writing the Features block
                writer.WriteLine("<Features>");
                for (int x = 0; x < toWrite.Features.Count; x++)
                {
                    Feature tmp = toWrite.Features[x];
                    writer.WriteLine("<Feature id=\"" + tmp.Id + "\" data=\"" + escapeInvalidXML(tmp.Name) + "\">");
                    //Neighbor
                    writer.WriteLine("<neighbors>");
                    for (int y = 0; y < tmp.Neighbors.Count; y++)
                    {
                        int id = toWrite.getFeatureIndex(tmp.Neighbors[y].Item1.Id);
                        writer.WriteLine("<neighbor dest=\"" + id + "\" weight=\"" + tmp.Neighbors[y].Item2 + "\" relationship=\"" + escapeInvalidXML(tmp.Neighbors[y].Item3) + "\"/>");
                    }//end for
                    writer.WriteLine("</neighbors>");
                    //Parent 
                    writer.WriteLine("<parents>");
                    for (int y = 0; y < tmp.Parents.Count; y++)
                    {
                        int id = toWrite.getFeatureIndex(tmp.Parents[y].Item1.Id);
                        writer.WriteLine("<parent dest=\"" + id + "\" weight=\"" + tmp.Parents[y].Item2 + "\" relationship=\"" + escapeInvalidXML(tmp.Parents[y].Item3) + "\"/>");
                    }//end for
                    writer.WriteLine("</parents>");
                    //Tag
                    List<Tuple<string, string, string>> tags = tmp.Tags;
                    for (int y = 0; y < tags.Count; y++)
                    {
                        string toWriteTag = "<tag key=\"" + escapeInvalidXML(tags[y].Item1);
                        toWriteTag += "\" value=\"" + escapeInvalidXML(tags[y].Item2);
                        toWriteTag += "\" type=\"" + escapeInvalidXML(tags[y].Item3) + "\"/>";
                        writer.WriteLine(toWriteTag);
                    }
                    //Speak
                    List<string> speaks = tmp.Speaks;
                    for (int y = 0; y < speaks.Count; y++)
                    {
                        writer.WriteLine("<speak value=\"" + escapeInvalidXML(speaks[y]) + "\"/>");
                    }
                    writer.WriteLine("</Feature>");
                }//end for
                //Stop writing the Features block
                writer.WriteLine("</Features>");

                writer.WriteLine("</AIMind>");
                writer.Close();
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                return false;
            }
        }

        //Reads an XML file at the given file path and creates a feature graph from it.
        public static FeatureGraph readFeatureGraph(string toReadPath)
        {
            try
            {
                FeatureGraph result_graph = new FeatureGraph();
                XmlDocument doc = new XmlDocument();
                doc.Load(toReadPath);
                docOld = doc;
                //Get the features
                XmlNodeList features = doc.SelectNodes("AIMind");
                features = features[0].SelectNodes("Features");
                features = features[0].SelectNodes("Feature");
                //Get each feature's name ("data" field) and each feature's id. Create a new feature
                //in the backend using the name and id.
                //Each feature must be created with a name and id first for neighbor and
                //parent relationships to be properly made.
                foreach (XmlNode node in features)
                {
                    string name = unEscapeInvalidXML(node.Attributes["data"].Value);
                    int id = Convert.ToInt32(node.Attributes["id"].Value);
                    result_graph.addFeature(new Feature(name, id));
                }//end foreach
                foreach (XmlNode node in features)
                {
                    //Find the current feature in the feature graph by its id.
                    Feature tmp = result_graph.getFeature(Convert.ToInt32(node.Attributes["id"].Value));
                    //Neighbor
                    XmlNodeList neighbors = node.SelectNodes("neighbors");
                    neighbors = neighbors[0].SelectNodes("neighbor");
                    foreach (XmlNode neighborNode in neighbors)
                    {
                        int neighbor_id = Convert.ToInt32(neighborNode.Attributes["dest"].Value);
                        double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
                        string relationship = "";
                        if (neighborNode.Attributes["relationship"] != null)
                        {
                            relationship = unEscapeInvalidXML(Convert.ToString(neighborNode.Attributes["relationship"].Value));
                        }//end if
                        //Add the neighbor feature according to its id
                        tmp.addNeighbor(result_graph.getFeature(neighbor_id), weight, relationship);

                        //pre-process in case no parent exist
                        foreach (XmlNode tempNode in features)
                        {
                            if (tempNode.Attributes["data"].Value == result_graph.Features[neighbor_id].Name)
                            {
                                XmlNodeList tempParents = tempNode.SelectNodes("parents");
                                if (tempParents.Count != 0)
                                {
                                    tempParents = tempParents[0].SelectNodes("parent");
                                    if (tempParents.Count == 0)
                                    {
                                        //ZEV: Check that this works!
                                        result_graph.getFeature(neighbor_id).addParent(tmp);
                                    }
                                }//end if
                            }
                        }
                        //result.Features[id].addNeighbor(tmp,weight);
                    }//end foreach
                    //Parent
                    XmlNodeList parents = node.SelectNodes("parents");
                    if (parents.Count != 0)
                    {
                        parents = parents[0].SelectNodes("parent");
                        foreach (XmlNode parentNode in parents)
                        {
                            int parent_id = Convert.ToInt32(parentNode.Attributes["dest"].Value);
                            double weight = Convert.ToDouble(parentNode.Attributes["weight"].Value);
                            string relationship = "";
                            if (parentNode.Attributes["relationship"] != null)
                            {
                                relationship = unEscapeInvalidXML(Convert.ToString(parentNode.Attributes["relationship"].Value));
                            }
                            tmp.addParent(result_graph.getFeature(parent_id), weight, relationship);
                        }
                    }//end if
                    //Tag
                    XmlNodeList tags = node.SelectNodes("tag");
                    foreach (XmlNode tag in tags)
                    {
                        string key = unEscapeInvalidXML(tag.Attributes["key"].Value);
                        string val = unEscapeInvalidXML(tag.Attributes["value"].Value);
                        string type = unEscapeInvalidXML(tag.Attributes["type"].Value);
                        tmp.addTag(key, val, type);
                    }
                    //Speak
                    XmlNodeList speaks = node.SelectNodes("speak");
                    foreach (XmlNode speak in speaks)
                    {
                        tmp.addSpeak(unEscapeInvalidXML(speak.Attributes["value"].Value));
                    }
                }
                int rootId = -1;
                try
                {
                    features = doc.SelectNodes("AIMind");
                    rootId = Convert.ToInt32(features[0].SelectNodes("Root")[0].Attributes["id"].Value);
                }
                catch (Exception) { }
                if (rootId != -1) { result_graph.Root = result_graph.getFeature(rootId); }
                return result_graph;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                return null;
            }
        }


        /* merge two files*/
        public static FeatureGraph readFeatureGraph2(string toReadPath)
        {
            try
            {
                FeatureGraph result = new FeatureGraph();
                XmlDocument doc = new XmlDocument();//doc is the second document, the one selected to merge with after a file has been opened
                doc.Load(toReadPath);
                XmlNodeList features = doc.SelectNodes("AIMind");
                features = features[0].SelectNodes("Features");
                features = features[0].SelectNodes("Feature");
                int countUp = 0;
                int countUp2 = 0;
                int countD = 0;
                XmlNodeList features2 = docOld.SelectNodes("AIMind");



                if (features2[0] != null)
                {//if the first document opened has features{
                    features2 = features2[0].SelectNodes("Feature");///this is put here because it would cause a crash outside if there were no features
                    foreach (XmlNode node in features2)
                    {
                        string id = node.Attributes["data"].Value;
                        result.addFeature(new Feature(id));
                        countD++;
                    }
                }
                foreach (XmlNode node in features){
                        bool checkifDuplicatesExist = false;
                        foreach (XmlNode nodePrime in features2){                      
                            string data1 = node.Attributes["data"].Value;                      
                            string data2 = nodePrime.Attributes["data"].Value;                       
                            if (data1 == data2)                    //if there are two datas with the same name, merge them
                            {                         
                                checkifDuplicatesExist = true;                       
                            }                   
                        }
                    if (checkifDuplicatesExist == false){//if there doesn't exist a version of the feature, add one
                        countUp++;
                        string id = node.Attributes["data"].Value;
                        result.addFeature(new Feature(id));
                        Feature tmp = result.getFeature(node.Attributes["data"].Value);
                        XmlNodeList neighbors = node.SelectNodes("neighbors");
                        neighbors = neighbors[0].SelectNodes("neighbor");
                        foreach (XmlNode neighborNode in neighbors){
                                int dest_number = Convert.ToInt32(neighborNode.Attributes["dest"].Value);// +countUp;// + countUp);
                                double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
                                tmp.addNeighbor(result.Features[dest_number], weight);
                                result.Features[dest_number].addParent(tmp);
                                //result.Features[id].addNeighbor(tmp, weight);
                        }
                        XmlNodeList tags = node.SelectNodes("tag");
                            foreach (XmlNode tag in tags)
                            {
                                string key = tag.Attributes["key"].Value;
                                string val = tag.Attributes["value"].Value;
                                string type = "0";
                                if (tag.Attributes["type"].Value == null)
                                {
                                    type = "0";
                                }
                                else
                                {
                                    type = tag.Attributes["type"].Value;
                                }
                                tmp.addTag(key, val, type);
                            }
                            XmlNodeList speaks = node.SelectNodes("speak");
                          
                        foreach (XmlNode speak in speaks){
                                tmp.addSpeak(speak.Attributes["value"].Value);
                            }
                        
                    }
                    else
                    {
                        countUp++;
                        Feature tmp = result.getFeature(node.Attributes["data"].Value);

                        XmlNodeList neighbors = node.SelectNodes("neighbors");
                        neighbors = neighbors[0].SelectNodes("neighbor");
                        foreach (XmlNode neighborNode in neighbors)
                        {
                            int id = Convert.ToInt32(neighborNode.Attributes["dest"].Value);// +countUp;// + countUp);
                            double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
                            tmp.addNeighbor(result.Features[id], weight);
                            result.Features[id].addParent(tmp);
                            //result.Features[id].addNeighbor(tmp,weight);
                        }

                        XmlNodeList tags = node.SelectNodes("tag");
                        foreach (XmlNode tag in tags)
                        {
                            string key = tag.Attributes["key"].Value;
                            string val = tag.Attributes["value"].Value;
                            string type = "0";
                            if (tag.Attributes["type"].Value == null)
                            {
                                type = "0";
                            }
                            else
                            {
                                type = tag.Attributes["type"].Value;
                            }
                            tmp.addTag(key, val, type);
                        }

                    }
                    
                }


                docOld = doc;
                //after loading the data from the two documents, run through the nodes found
                foreach (XmlNode node in features2)///add the features from the second file
                {
                    Feature tmp = result.getFeature(node.Attributes["data"].Value);
                    XmlNodeList neighbors = node.SelectNodes("neighbors");
                    neighbors = neighbors[0].SelectNodes("neighbor");

                   string secDet = Convert.ToString(Convert.ToInt32(node.Attributes["id"].Value) + countUp);
                   

                    foreach (XmlNode neighborNode in neighbors)
                    {
                        int id = Convert.ToInt32(neighborNode.Attributes["dest"].Value) +countUp;// +0 + 1;
                        double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
                        
                          tmp.addNeighbor(result.Features[id], weight);
                          result.Features[id].addParent(tmp);//add neighbors to node
                        //result.Features[id].addNeighbor(tmp,weight);
                    }
                    XmlNodeList tags = node.SelectNodes("tag");
                    foreach (XmlNode tag in tags)
                    {
                        string key = tag.Attributes["key"].Value;
                        string val = tag.Attributes["value"].Value;
                        string type = "0";
                        if (tag.Attributes["type"].Value == null)
                        {
                            type = "0";
                        }
                        else
                        {
                            type = tag.Attributes["type"].Value;
                        }
                        tmp.addTag(key, val, type);
                    }
                    XmlNodeList speaks = node.SelectNodes("speak");
                    foreach (XmlNode speak in speaks)
                    {
                        tmp.addSpeak(speak.Attributes["value"].Value);
                    }
                }

                foreach (XmlNode node in features2)
                {
          
                }

                int rootId = -1;
                try
                {
                    features = doc.SelectNodes("AIMind");
                    rootId = Convert.ToInt32(features[0].SelectNodes("Root")[0].Attributes["id"].Value);
                }
                catch (Exception) { }
                if (rootId != -1) { result.Root = result.getFeature(rootId); }
                return result;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
                return null;
            }
        }
    }
}
