﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dialogue_Data_Entry;
using AIMLbot;
using System.Collections;
using System.Diagnostics;

namespace Dialogue_Data_Entry
{
    enum Direction : int
    {
        NORTH = 1, SOUTH = -1,
        EAST = 2, WEST = -2,
        NORTHEAST = 3, SOUTHWEST = -3,
        NORTHWEST = 4, SOUTHEAST = -4,
        CONTAIN = 5, INSIDE = -5,
        HOSTED = 6, WAS_HOSTED_AT = -6,
        WON = 0
    }
    enum Question : int
    {
        WHAT = 0, WHERE = 1, WHEN = 2
    }

    /// <summary>
    /// A data structure to hold information about a query
    /// </summary>
    class Query
    {
        // The name of the Feature that the user asked about
        public Feature MainTopic { get; private set; }
        // Whether or not the input was an explicit question
        public bool IsQuestion { get { return QuestionType != null; } }
        // The type of Question
        public Question? QuestionType { get; private set; }
        // The direction/relationship asked about.
        public Direction? Direction { get; private set; }
        public bool HasDirection { get { return Direction != null; } }

        public Query(Feature mainTopic, Question? questionType, Direction? directions)
        {
            MainTopic = mainTopic;
            QuestionType = questionType;
            Direction = directions;
        }
        public override string ToString()
        {
            string s = "Topic: " + MainTopic.Data;
            s += "\nQuestion type: " + QuestionType ?? "none";
            s += "\nDirection specified: " + Direction ?? "none";
            return s;
        }
    }

    /// <summary>
    /// A utility class to parse natural input into a Query and a Query into natural output.
    /// </summary>
    class QueryHandler
    {
        private const string FORMAT = "FORMAT:";
        private const string IDK = "I'm afraid I don't know anything about that topic.";
        private const string IDK_CN = "对不起，我不知道。";
        private string[] punctuation = { ",", ";", ".", "?", "!", "\'", "\"", "(", ")", "-" };
        private string[] questionWords = { "?", "what", "where", "when" };
        private string[] directionWords = {"inside", "contain", "north", "east", "west", "south",
                                      "northeast", "northwest", "southeast", "southwest",
                                      "hosted", "was_hosted_at", "won"};
        private string[] Directional_Words = { "is southwest of", "is southeast of"
                , "is northeast of", "is north of", "is west of", "is east of", "is south of", "is northwest of" };
        // "is in" -> contains?
        private Bot bot;
        private User user;
        private FeatureGraph graph;
        private Feature topic;
        private List<string> features;
        private string[] _buffer;
        private string[] buffer { get { return _buffer; } set { _buffer = value; b = 0; } }
        private int b;  // buffer index gets reset when buffer does
        private int turn;
        private int noveltyAmount = 5;
        private List<TemporalConstraint> temporalConstraintList;
        private List<string> topicHistory = new List<string>();
        private string prevSpatial;

        public LinkedList<Feature> prevCurr = new LinkedList<Feature>();
		public LinkedList<Feature> MetList = new LinkedList<Feature>();
		public int countFocusNode = 0;
		public double noveltyValue = 0.0;

        //A list of string lists, each of which represents a set of relationship
        //words which may be interchangeable when used to find analogies.
        public List<List<string>> equivalent_relationships = new List<List<string>>();

        //FILTERING:
        //A list of nodes to filter out of mention.
        //Nodes in this list won't be spoken explicitly unless they
        //are directly queried for.
        //These nodes are still included in traversals, but upon traveling to
        //one of these nodes the next step in the traversal is automatically taken.
        public List<string> filter_nodes = new List<string>();
        //A list of relationships which should not be used for analogies.
        public List<String> no_analogy_relationships = new List<string>();

        //JOINT MENTIONS:
        //A list of feature lists, each of which represent
        //nodes that should be mentioned together
        public List<List<Feature>> joint_mention_sets = new List<List<Feature>>();

        //Which language we are operating in.
        //Default is English.
        public int language_mode = Constant.EnglishMode;

        /// <summary>
        /// Create a converter for the specified XML file
        /// </summary>
        /// <param name="xmlFilename"></param>
        public QueryHandler(FeatureGraph graph, List<TemporalConstraint> myTemporalConstraintList)
        {
            // Load the AIML Bot
            this.bot = new Bot();
            this.temporalConstraintList = myTemporalConstraintList;
            bot.loadSettings();
            bot.isAcceptingUserInput = false;
            bot.loadAIMLFromFiles();
            bot.isAcceptingUserInput = true;
            this.user = new User("user", this.bot);

            // Load the Feature Graph
            this.graph = graph;

            // Feature Names, with which to index the graph
            this.features = graph.getFeatureNames();

            this.turn = 1;
            this.topic = null;

            //Build lists of equivalent relationships
            //is, are, was, is a kind of, is a
            equivalent_relationships.Add(new List<string>() { "is", "are", "was", "is a kind of", "is a" });
            //was a member of, is a member of
            equivalent_relationships.Add(new List<string>() { "was a member of", "is a member of" });
            //won a gold medal in, won
            equivalent_relationships.Add(new List<string>() { "won a gold medal in", "won" });
            //is one of, was one of the, was one of
            equivalent_relationships.Add(new List<string>() { "is one of", "was one of the", "was one of" });
            //include, includes, included
            equivalent_relationships.Add(new List<string>() { "include", "includes", "included" });
            //took place on
            equivalent_relationships.Add(new List<string>() { "took place on" });
            //took place at
            equivalent_relationships.Add(new List<string>() { "took place at" });
            //has, had
            equivalent_relationships.Add(new List<string>() { "has", "had" });
            //includes event
            equivalent_relationships.Add(new List<string>() { "includes event" });
            //includes member, included member
            equivalent_relationships.Add(new List<string>() { "includes member", "included member" });
            //include athlete
            equivalent_relationships.Add(new List<string>() { "include athlete" });
            //is southwest of, is southeast of, is northeast of, is north of,
            //is west of, is east of, is south of, is northwest of
            equivalent_relationships.Add(new List<string>() { "is southwest of", "is southeast of"
                , "is northeast of", "is north of", "is west of", "is east of", "is south of", "is northwest of" });

            //Build list of filter nodes.
            //Each filter node is identified by its Data values in the XML
            filter_nodes.Add("Male");
            filter_nodes.Add("Female");
            filter_nodes.Add("Cities");
            filter_nodes.Add("Sports");
            filter_nodes.Add("Gold Medallists");
            filter_nodes.Add("Venues");
            filter_nodes.Add("Time");

            //Build list of relationships which should not be used in analogies.
            no_analogy_relationships.Add("occurred before");
            no_analogy_relationships.Add("occurred after");
        }

		private string LeadingTopic(Feature last, Feature first)
		{
			string return_message = "";
            
            string first_data = first.Data;
            if (first_data.Contains("##"))
            {
                if (language_mode == 0) { first_data = first_data.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { first_data = first_data.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            string last_data = last.Data;
            if (last_data.Contains("##"))
            {
                if (language_mode == 0) { last_data = last_data.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { last_data = last_data.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }

            // 8.18: replace first.Data with first_data, last.Data with last_data,
            // last.getRelationshipNeighbor(first.Data) with relationship

            //First is the current node (the one that has just been traversed to)
            //A set of possible lead-in statements.
            List<string> lead_in_statements = new List<string>();
            if(language_mode == 0)
            {
                lead_in_statements.Add("{There's also " + first_data + ".} ");
                lead_in_statements.Add("{But let's talk about " + first_data + ".} ");
                lead_in_statements.Add("{And have I mentioned " + first_data + "?} ");
                lead_in_statements.Add("{Now, about " + first_data + ".} ");
                lead_in_statements.Add("{Now, let's talk about " + first_data + ".} ");
                lead_in_statements.Add("{I should touch on " + first_data + ".} ");
                lead_in_statements.Add("{Have you heard of " + first_data + "?} ");
            }
            else
            {
                lead_in_statements.Add("{还有" + first_data + "呢。} ");
                lead_in_statements.Add("{让我们谈论" + first_data + "吧。} ");
                lead_in_statements.Add("{我刚刚有提到过" + first_data + "吗？} ");
                lead_in_statements.Add("{关于" + first_data + "。} ");
                lead_in_statements.Add("{现在让我们谈谈" + first_data + "吧。} ");
                lead_in_statements.Add("{我觉得有必要提一下" + first_data + "。} ");
                lead_in_statements.Add("{你有听说过" + first_data + "吗？} ");
            }


            //A set of lead-in statements for non-novel nodes
            List<string> non_novel_lead_in_statements = new List<string>();
            if(language_mode == 0)
            {
                non_novel_lead_in_statements.Add("{There's also " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{Let's talk about " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{I'll mention " + first_data + " real quick.} ");
                non_novel_lead_in_statements.Add("{So, about " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{Now then, about " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{Let's talk about " + first_data + " for a moment.} ");
                non_novel_lead_in_statements.Add("{Have I mentioned " + first_data + "?} ");
                non_novel_lead_in_statements.Add("{Now, about " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{Now, let's talk about " + first_data + ".} ");
                non_novel_lead_in_statements.Add("{I should touch on " + first_data + ".} ");
            }
            else
            {
                non_novel_lead_in_statements.Add("{还有" + first_data + "呢。} ");
                non_novel_lead_in_statements.Add("{让我们谈谈" + first_data + "吧。} ");
                non_novel_lead_in_statements.Add("{我想简要提提" + first_data + "。} ");
                non_novel_lead_in_statements.Add("{然后,关于" + first_data + "。} ");
                non_novel_lead_in_statements.Add("{现在谈谈" + first_data + "吧。} ");
                non_novel_lead_in_statements.Add("{让我们聊一会儿" + first_data + " 吧。} ");
                non_novel_lead_in_statements.Add("{我刚刚有提到" + first_data + "吗？} ");
                non_novel_lead_in_statements.Add("{那么," + first_data + "。} ");
                non_novel_lead_in_statements.Add("{现在让我们谈谈" + first_data + "吧。} ");
                non_novel_lead_in_statements.Add("{我该提及" + first_data + "。} ");
            }


            //A set of lead-in statements for novel nodes
            //TODO: Author these again; things like let's talk about something different now.
            List<string> novel_lead_in_statements = new List<string>();
            if(language_mode == 0)
            {
                novel_lead_in_statements.Add("{Let's talk about something different. ");
                novel_lead_in_statements.Add("{Let's switch gears. ");
            }
            else
            {
                novel_lead_in_statements.Add("{让我们谈点别的吧。");
                novel_lead_in_statements.Add("{让我们聊点别的吧。");
                novel_lead_in_statements.Add("{让我们说点别的什么吧。");
                novel_lead_in_statements.Add("{让我们换个话题吧。");
            }


            Random rand = new Random();

			// Check if there is a relationship between two nodes
			if (last.getNeighbor(first.Data) != null || first.getNeighbor(last.Data) != null)
			{
                string relationship = last.getRelationshipNeighbor(first.Data);
                if (relationship.Contains("##"))
                {
                    if (language_mode == 0) { relationship = relationship.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                    else { relationship = relationship.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
                }

                // Check if last has first as its neighbor
                if (!last.getRelationshipNeighbor (first.Data).Equals("")
                    && !(last.getRelationshipNeighbor (first.Data) == null))
                {
					return_message = "{" + last_data + " " + relationship + " " 
						+ first_data + ".} ";
                    return return_message;
				}//end if
				// If last is a child node of first (first is a parent of last)
				else if (!last.getRelationshipParent (first.Data).Equals("")
                            && !(last.getRelationshipParent(first.Data) == null))
				{
					return_message = "{" + last_data + " " + relationship + " " 
						+ first_data + ".} ";
                    return return_message;
				}//end else if
			}//end if
			// Neither neighbor or parent/child
			// NEED TO consider novelty value (low)
			//else if (last.getNeighbor(first.Data) == null || first.getNeighbor(last.Data) == null)

            //If the novelty is high enough, always include a novel topic lead-in statement.
            if (noveltyValue >= 0.6)
                return_message += novel_lead_in_statements[rand.Next(novel_lead_in_statements.Count)];
            //Otherwise, include a non-novel topic lead-in statement.
            else
            {
                return_message += non_novel_lead_in_statements[rand.Next(non_novel_lead_in_statements.Count)];
            }//end if

            //!FindSpeak(first).Contains<string>(first.Data)

			return return_message;
		}

		private string RelationshipAnalogy(Feature old, Feature newOld, Feature prevOfCurr, Feature current)
		{
			string return_message = "";
			/*Console.WriteLine("old: " + old.Data);
			Console.WriteLine("new: " + newOld.Data);
            Console.WriteLine("relationship: " + old.getRelationshipNeighbor(newOld.Data));
			Console.WriteLine("previous of current: " + prevOfCurr.Data);
			Console.WriteLine("current: " + current.Data);
            Console.WriteLine("relationship: " + prevOfCurr.getRelationshipNeighbor(current.Data));
            */

			// Senten Patterns list - for 3 nodes
			List<string> sentencePatterns = new List<string>();

			Random rnd = new Random();

            //Define A1, B1, A2, B2, R1,and R2.
            //  Node A1 has relationship R1 with node B1.
            //  Node A2 has relaitonship R2 with node B2.
			//  AND R1 and R2 are in the same list inside equivalent_relationship list.
            string a1 = "";
            string b1 = "";
            string a2 = "";
            string b2 = "";
            string r1 = "";
			string r2 = "";


            /*(old.getRelationshipNeighbor(newOld.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data))
                            || newOld.getRelationshipNeighbor(old.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data))
                            || newOld.getRelationshipNeighbor(old.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data))
                            || old.getRelationshipNeighbor(newOld.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data))*/

			//Check equivalent and similarity
			bool found = false;
            bool directional = false;
            //Check if the relationship is a directional word.
            if (Directional_Words.Contains(old.getRelationshipNeighbor(newOld.Data))
                || Directional_Words.Contains(newOld.getRelationshipNeighbor(old.Data)))
            {
                directional = true;
            }//end if
            

			foreach (List<string> list in equivalent_relationships)
			{
				if (found == true) break;
				if ((list.Contains(old.getRelationshipNeighbor(newOld.Data)) && list.Contains(prevOfCurr.getRelationshipNeighbor(current.Data)))
					|| old.getRelationshipNeighbor(newOld.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data)))
				{
					a1 = old.Data;
					b1 = newOld.Data;
					a2 = prevOfCurr.Data;
					b2 = current.Data;
					r1 = old.getRelationshipNeighbor(newOld.Data);
					r2 = prevOfCurr.getRelationshipNeighbor(current.Data);
					found = true;
				}
				else if ((list.Contains(newOld.getRelationshipNeighbor(old.Data)) && list.Contains(current.getRelationshipNeighbor(prevOfCurr.Data)))
					|| newOld.getRelationshipNeighbor(old.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data)))
				{
					a1 = newOld.Data;
					b1 = old.Data;
					a2 = current.Data;
					b2 = prevOfCurr.Data;
					r1 = newOld.getRelationshipNeighbor(old.Data);
					r2 = current.getRelationshipNeighbor(prevOfCurr.Data);
					found = true;
				}
				else if ((list.Contains(newOld.getRelationshipNeighbor(old.Data)) && list.Contains(prevOfCurr.getRelationshipNeighbor(current.Data)))
					|| newOld.getRelationshipNeighbor(old.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data)))
				{
					a1 = newOld.Data;
					b1 = old.Data;
					a2 = prevOfCurr.Data;
					b2 = current.Data;
					r1 = newOld.getRelationshipNeighbor(old.Data);
					r2 = prevOfCurr.getRelationshipNeighbor(current.Data);
					found = true;
				}
				else if ((list.Contains(old.getRelationshipNeighbor(newOld.Data)) && list.Contains(current.getRelationshipNeighbor(prevOfCurr.Data)))
					|| old.getRelationshipNeighbor(newOld.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data)))
				{
					a1 = old.Data;
					b1 = newOld.Data;
					a2 = current.Data;
					b2 = prevOfCurr.Data;
					r1 = old.getRelationshipNeighbor(newOld.Data);
					r2 = current.getRelationshipNeighbor (prevOfCurr.Data);
					found = true;
				}
			}

			/*
            else if (old.getRelationshipNeighbor(newOld.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data)))
            {
                a1 = old.Data;
                b1 = newOld.Data;
                a2 = prevOfCurr.Data;
                b2 = current.Data;
                r1 = old.getRelationshipNeighbor(newOld.Data);
				r2 = prevOfCurr.getRelationshipNeighbor(current.Data);
            }//end if
            else if (newOld.getRelationshipNeighbor(old.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data)))
            {
                a1 = newOld.Data;
                b1 = old.Data;
                a2 = current.Data;
                b2 = prevOfCurr.Data;
                r1 = newOld.getRelationshipNeighbor(old.Data);
				r2 = current.getRelationshipNeighbor(prevOfCurr.Data);
            }//end else if
            else if (newOld.getRelationshipNeighbor(old.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data)))
            {
                a1 = newOld.Data;
                b1 = old.Data;
                a2 = prevOfCurr.Data;
                b2 = current.Data;
                r1 = newOld.getRelationshipNeighbor(old.Data);
				r2 = prevOfCurr.getRelationshipNeighbor(current.Data);
            }//end else if
            else if (old.getRelationshipNeighbor(newOld.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data)))
            {
                a1 = old.Data;
                b1 = newOld.Data;
                a2 = current.Data;
                b2 = prevOfCurr.Data;
                r1 = old.getRelationshipNeighbor(newOld.Data);
				r2 = current.getRelationshipNeighbor (prevOfCurr.Data);
            }//end else if
            */

            //If there is a blank relationship, no analogy may be made.
			if (r1.Equals("") || r2.Equals(""))
                return "";
            //if a1 equals a2 and b1 equals b2, no analogy may be made.
            if (a1.Equals(a2) && b1.Equals(b2))
                return "";
            //If the relationship is directional and b1 does NOT equal b2, then
            //no analogy may be made.
            if (directional && !(b1.Equals(b2)))
            {
                return "";
            }//end if

            //if (old.getRelationshipNeighbor(newOld.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data)) &&
            //	old.getRelationshipNeighbor(newOld.Data) != "" && prevOfCurr.getRelationshipNeighbor(current.Data) != "")
            //{
            //string relationship = old.getRelationshipNeighbor(newOld.Data);

            // enable bilingual mode

            if (a1.Contains("##"))
            {
                if (language_mode == 0) { a1 = a1.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { a1 = a1.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            if (b1.Contains("##"))
            {
                if (language_mode == 0) { b1 = b1.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { b1 = b1.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            if (a2.Contains("##"))
            {
                if (language_mode == 0) { a2 = a2.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { a2 = a2.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            if (b2.Contains("##"))
            {
                if (language_mode == 0) { b2 = b2.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { b2 = b2.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            if (r1.Contains("##"))
            {
                if (language_mode == 0) { r1 = r1.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { r1 = r1.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }
            if (r2.Contains("##"))
            {
                if (language_mode == 0) { r2 = r2.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                else { r2 = r2.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
            }

            // 4 nodes
            if(language_mode == 0)
            {
                sentencePatterns.Add("[Just as " + a1 + " " + r1 + " " + b1
                    + ", so too " + a2 + " " + r2 + " " + b2 + ".] ");
                sentencePatterns.Add("[" + a2 + " " + r2 + " " + b2
                    + ", much like " + a1 + " " + r1 + " " + b1 + ".] ");
                sentencePatterns.Add("[Like " + a1 + " " + r1 + " " + b1 + ", "
                    + a2 + " also " + r2 + " " + b2 + ".] ");
                sentencePatterns.Add("[The same way that " + a1 + " " + r1 + " " + b1
                    + ", " + a2 + " " + r2 + " " + b2 + ".] ");
                sentencePatterns.Add("[Remember how " + a1 + " " + r1 + " " + b1
                    + "? Well, in the same way, " + a2 + " also " + r2 + " " + b2 + ".] ");
                sentencePatterns.Add("[" + a2 + " also " + r2 + " " + b2
                    + ", similar to how " + a1 + " " + r1 + " " + b1 + ".] ");
            }
            else
            {
                sentencePatterns.Add("[像" + a1 + r1 + b1 + "一样," + a2 + "也" + r2 + b2 + "。] ");
                sentencePatterns.Add("[就像" + a1 + r1 + b1 + "一样," + a2 + r2 + b2 + "。] ");
                sentencePatterns.Add("[就像" + a1 + r1 + b1 + "一样," + a2 + "也" + r2 + b2 + "。] ");
                sentencePatterns.Add("[就如同" + a1 + r1 + b1 + "一样," + a2 + r2 + b2 + "。] ");
                sentencePatterns.Add("[如同" + a1 + r1 + b1 + "一般," + a2 + r2 + b2 + "。] ");
                sentencePatterns.Add("[正像" + a1 + r1 + b1 + "一样," + a2 + r2 + b2 + "。] ");
                sentencePatterns.Add("[" + a2 + r2 + b2 + "," + "正像" + a1 + r1 + b1 + "。] ");
            }

			int random_int = rnd.Next(sentencePatterns.Count);

            return_message += sentencePatterns[random_int];
			//}

            //DEBUG
            Console.WriteLine("return_message: " + return_message);

			return return_message;
		}

		// Check to see if the name of the node is already mentioned in the speaks
		public bool CheckAlreadyMentioned(Feature feat)
		{
			List<string> speaks = feat.Speaks;
			string data = feat.Data;

            //Console.WriteLine(feat.Data + " mentioned in " + speaks[0] + " : " + speaks[0].Contains (data));

			return speaks[0].Contains (data);
		}
			
	    private string MessageToServer(Feature feat, string speak, string noveltyInfo, string proximalInfo = "", bool forLog = false, bool out_of_topic_response = false)
        {
            String return_message = "";

            prevCurr.AddFirst(feat);
	    	MetList.AddLast(feat);
	    	countFocusNode += 1;

            if (prevCurr.Count > 2)
            {
		        prevCurr.RemoveLast();
	        }
            //Store the last history_size number of nodes
            int history_size = 100;
            if (MetList.Count > history_size)
			{
				MetList.RemoveFirst();
			}

			// Previous-Current nodes
            Feature first = prevCurr.First();   // Current node
            Feature last = prevCurr.Last();     // Previous node

			// Metaphor - 3 nodes
			Feature old = MetList.First();
            Feature newOld = null;
			//int countNode = 1;
			if (MetList.Count () >= 2)
			{
				newOld = MetList.ElementAt(1);
			}
			Feature current = MetList.Last();
			// 4th node
			// NEED TO check all possibilities (17 pairs - linear time)

            Feature prevOfCurr = null;
            if (MetList.Count() >= 2)
                prevOfCurr = MetList.ElementAt(MetList.Count - 2);

            bool analogy_made = false;
            if (MetList.Count() >= 4)
            {
				// Analogy
				if (newOld != null )
				{

				}

				//while (old.getRelationshipNeighbor(newOld.Data) != prevOfCurr.getRelationshipNeighbor(current.Data))
                //DEBUG
                if (prevOfCurr.getNeighbor(current.Data) != null)
                {
                    Console.WriteLine(prevOfCurr.Data + " is neighbors with " + current.Data + ", relationship " + prevOfCurr.getRelationshipNeighbor(current.Data));
                }//end if

                for (int countNode = 0; countNode < MetList.Count - 1; countNode++ )
                {
                    old = MetList.ElementAt(countNode);
                    newOld = MetList.ElementAt(countNode + 1);
                    //countNode += 1;
                    if (old.Data.Equals(prevOfCurr.Data) && newOld.Data.Equals(current.Data))
                    {
                        continue;
                        //countNode = 1;
                        //break;
                    }
                    //Check the no_analogy list first to see if an analogy should be made with this relationship.
                    if (no_analogy_relationships.Contains(old.getRelationshipNeighbor(newOld.Data)))
                    {
                        continue;
                    }//end if

                    //If the relationships match and neither relationship is the empty relationship,
                    //form an analogy.
                    //NOTE: Checking relationships in BOTH directions
                    bool try_analogy = false;
                    foreach (List<String> equivalent_set in equivalent_relationships)
                    {
                        //Check if the relationships are in the same equivalent set. If so, try to form an analogy.
                        if (equivalent_set.Contains(old.getRelationshipNeighbor(newOld.Data))
                            && equivalent_set.Contains(prevOfCurr.getRelationshipNeighbor(current.Data)))
                        {
                            try_analogy = true;
                            break;
                        }//end if
                    }//end foreach
                    if (((old.getRelationshipNeighbor(newOld.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data))
                            || newOld.getRelationshipNeighbor(old.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data))
                            || newOld.getRelationshipNeighbor(old.Data).Equals(prevOfCurr.getRelationshipNeighbor(current.Data))
                            || old.getRelationshipNeighbor(newOld.Data).Equals(current.getRelationshipNeighbor(prevOfCurr.Data)))
                        && old.getRelationshipNeighbor(newOld.Data) != "" && prevOfCurr.getRelationshipNeighbor(current.Data) != "")
                        || try_analogy)
                    {
                        //countNode = 1;
                        
                        // Count relationship in the list (<=20 nodes)
						int count_relationship = 0;
						int cc = 0;
						while (cc <= MetList.Count())
						{
							if (old.getRelationshipNeighbor (newOld.Data) == prevOfCurr.getRelationshipNeighbor (current.Data))
							{
								count_relationship += 1;
							}
							cc += 1;

						}
						// Only display rare
                        // Not necessary at the moment to check for rareness of analogy
						if (count_relationship <= 1000)
						{
                            int return_message_length = return_message.Length;
							return_message += RelationshipAnalogy (old, newOld, prevOfCurr, current);
                            //If any addition has been made to the return message, then an
                            //analogy has been successfully made.
                            if (return_message.Length > return_message_length)
                                analogy_made = true;
                            //Otherwise, keep trying to find an analogy
                            else
                                continue;
						}
						break;
                    }//end if
                }
            }

            Console.WriteLine("analogy made " + analogy_made);
			// Leading-topic sentence.
            // Only place a leading topic sentence if there isn't already an analogy here.
            if (prevCurr.Count > 1 && !analogy_made) // && !CheckAlreadyMentioned(current))// && countFocusNode == 1)
            {
                Console.WriteLine("creating leading topic for " + last.Data + " to " + first.Data);
                return_message = LeadingTopic(last, first);
                countFocusNode = 0; // Set back to 0
            }
            //Otherwise, this is the first node being mentioned.
            else if (!analogy_made)
            {
                //As the first node, place an introduction phrase before it.
                // 8.18: replaced first.Data with first_data
                string first_data = first.Data;
                if (first_data.Contains("##"))
                {
                    if(language_mode == 0) { first_data = first_data.Split(new string[] { "##" }, StringSplitOptions.None)[0]; }
                    else { first_data = first_data.Split(new string[] { "##" }, StringSplitOptions.None)[1]; }
                }
                if(language_mode == 0) { return_message = "{First, let's talk about " + first_data + ".} "; }
                else { return_message = "{首先，让我们谈谈 " + first_data + "。} "; }
            }//end else


            String to_speak = return_message + speak;

            if (out_of_topic_response)
            {
                //"I'm afraid I don't know anything about ";
                if(language_mode == 0)
                {
                    to_speak = "I'm sorry, I'm afraid I don't understand what you are asking. But here's something I do know about. "
                       + to_speak;
                }
                else
                {
                    to_speak = "对不起，我不知道您在说什么。但我知道这些。" + to_speak;
                }

            }//end if

            if (forLog)
                return_message = to_speak + "\r\n";
            else
                return_message = " ID:" + this.graph.getFeatureIndex(feat.Data) + ":Speak:" + to_speak + ":Novelty:" + noveltyInfo + ":Proximal:" + proximalInfo;

            //Console.WriteLine("to_speak: " + to_speak);

            return return_message;
        }

        //update various history when the system choose the next topic
        public void updateHistory(Feature nextTopic)
        {
            //update spatial constraint information
            bool spatialExist = false;
            if (topicHistory.Count() > 0)
            {
                Feature prevTopic = graph.getFeature(topicHistory[topicHistory.Count() - 1]);
                if (prevTopic.getNeighbor(nextTopic.Data) != null)
                {
                    foreach(string str in Directional_Words)
                    {
                        if (str == prevTopic.getRelationshipNeighbor(nextTopic.Data))
                        {
                            prevSpatial = str;
                            spatialExist = true;
                            break;
                        }
                    }
                }
            }
            if (!spatialExist)
            {
                prevSpatial = "";
            }

            //update temporal constraint information
            FeatureSpeaker temp = new FeatureSpeaker(this.graph, temporalConstraintList);
            List<int> temporalIndex = temp.temporalConstraint(nextTopic,turn,topicHistory);
            for (int x = 0; x < temporalIndex.Count(); x++)
            {
                temporalConstraintList[temporalIndex[x]].Satisfied = true;
            }
            //update topic's history
            topicHistory.Add(nextTopic.Data);
        }

        //Form2 calls this function
        //input is the input to be parsed.
        //messageToServer indicates whether or not we are preparing a response to the front-end.
        //forLog indicates whether or not we are preparing a response for a log output.
        //outOfTopic indicates whether or not we are continuing out-of-topic handling.
        //projectAsTopic true means we use forward projection to choose the next node to traverse to based on
        //  how well the nodes in the n-length path from the current node relate to the current node.
        public string ParseInput(string input, bool messageToServer = false, bool forLog = false, bool outOfTopic = false, bool projectAsTopic = false)
        {
            string answer;
            if(language_mode == 0) { answer = IDK; }
            else { answer = IDK_CN; }
            string noveltyInfo = "";
            double currentTopicNovelty = -1;
            // Pre-processing

            //Console.WriteLine("parse input " + input);

            //The input may be delimited by colons. Try to split it.
            String[] split_input = input.Trim().Split(':');
            //Console.WriteLine("split input " + split_input[0]);

            // Lowercase for comparisons
            input = input.Trim().ToLower();
            //Console.WriteLine("trimmed lowered input " + input);

            if (!string.IsNullOrEmpty(input))
            {
                // Check to see if the AIML Bot has anything to say
                Request request = new Request(input, this.user, this.bot);
               
                Result result = bot.Chat(request);
                string output = result.Output;
                
                if (output.Length > 0)
                {
                    if (!output.StartsWith(FORMAT))
                        return output;
                    
                    //MessageBox.Show("Converted output reads: " + output);
                    input = output.Replace(FORMAT, "").ToLower();
                }
            }

            // Remove punctuation
            input = RemovePunctuation(input);
            
            // Check
            if (this.topic == null)
                this.topic = this.graph.Root;
            FeatureSpeaker speaker = new FeatureSpeaker(this.graph, temporalConstraintList, prevSpatial, topicHistory);

            if (split_input.Length != 0 || messageToServer)
            {
                //Step-through command from Query window.
                if (split_input[0].Equals("STEP"))
                {
                    //Step through the program with blank inputs a certain number of times, 
                    //specified by the second argument in the command
                    //Console.WriteLine("step_count " + split_input[1]);
                    int step_count = int.Parse(split_input[1]);

                    //TESTING JOINT MENTIONS
                    //If there are two more colon-separated integers in the command, they are two node IDs that should be mentioned together.
                    if (split_input.Length > 2)
                    {
                        //Since this is just a test, first, clear joint_mention_sets
                        joint_mention_sets.Clear();
                        //Get the two indices from the command
                        int index_1 = int.Parse(split_input[2]);
                        int index_2 = int.Parse(split_input[3]);
                        //Add the pair as a list of features to joint_mention_sets.
                        List<Feature> joint_set = new List<Feature>();
                        joint_set.Add(this.graph.getFeature(index_1));
                        joint_set.Add(this.graph.getFeature(index_2));
                        joint_mention_sets.Add(joint_set);
                    }//end if

                    //Create an answer by calling the ParseInput function step_count times.
                    answer = "";
                    for (int s = 0; s < step_count; s++)
                    {
                        //Get forServer and forLog responses.
                        //Treat every 5th node as topic
                        if (s % 5 == 1)
                        {
                            //Last parameter true means the current node is the topic node
                            answer += ParseInput("", true, true, false, false);
                        }//end if
                        else
                            answer += ParseInput("", true, true, false, false);
                        answer += "\n";
                    }
                    //Console.WriteLine("answer " + answer);
                    //Just return this answer by itself
                    return answer;
                }//end if

                // GET_NODE_VALUES command from Unity front-end
                if (split_input[0].Equals("GET_NODE_VALUES"))
                {
                    //Get the node we wish to get a set of values for, by data.
                    //"data" is represented by each node's data field in the XML.
                    //In the split input string, index 1 is the data of the node we want
                    //to get values for.
                    //Index 2 is the data of the node we are getting values relative to.
                    string current_node_data = split_input[1];
                    string old_node_data = split_input[2];
                    //Get the features for these two nodes
					Feature current_feature = this.graph.getFeature(current_node_data);
					Feature old_feature = this.graph.getFeature(old_node_data);
                    //If EITHER feature is null, return an error message.
                    if (current_feature == null || old_feature == null)
                        return "no feature found";
                    double[] return_node_values = speaker.calculateScoreComponents(current_feature, old_feature);
                    //Turn them into a colon-separated string, headed by
                    //the key-phrase "RETURN_NODE_VALUES"
                    string return_string = return_node_values[Constant.ScoreArrayScoreIndex] + ":"
                        + return_node_values[Constant.ScoreArrayNoveltyIndex] + ":" 
                        + return_node_values[Constant.ScoreArrayDiscussedAmountIndex] + ":"
                        + return_node_values[Constant.ScoreArrayExpectedDramaticIndex] + ":" 
                        + return_node_values[Constant.ScoreArraySpatialIndex] + ":"
                        + return_node_values[Constant.ScoreArrayHierarchyIndex] + ":";
                    
                    return return_string;
                }//end if
                // GET_WEIGHT command from Unity front-end
                else if (split_input[0].Equals("GET_WEIGHT"))
                {
                    //Return a colon-separated string of every weight value
                    string return_string = "Weights: ";
                    double[] weight_array = this.graph.getWeightArray();
                    for (int i = 0; i < weight_array.Length; i++)
                    {
                        if (i != 0)
                            return_string += ":";
                        return_string += weight_array[i];
                    }//end for
                    return return_string;
                }//end else if
                // SET_WEIGHT command from Unity front-end
                else if (split_input[0].Equals("SET_WEIGHT"))
                {

                    //For each pair,
                    //Index 1 is the index of the weight we wish to adjust.
                    //Index 2 is the new weight value.
                    for (int m = 1; m < split_input.Length; m += 2)
                    {
                        this.graph.setWeight(int.Parse(split_input[m]), double.Parse(split_input[m + 1]));
                    }//end for

                    //Return the new weight values right away.
                    string return_string = "Weights: ";
                    double[] weight_array = this.graph.getWeightArray();
                    for (int i = 0; i < weight_array.Length; i++)
                    {
                        if (i != 0)
                            return_string += ":";
                        return_string += weight_array[i];
                    }//end for
                    return return_string;
                }//end else if
                //GET_RELATED command from Unity front-end.
                //Returns a message containing a list of most novel and most proximal nodes
                else if (split_input[0].Equals("GET_RELATED"))
                {
                    //GET_RELATED only gets related nodes for the current topic.
                    noveltyInfo = speaker.getNovelty(this.topic, this.turn, noveltyAmount);
                    return "Novelty:" + noveltyInfo + ":Proximal:" + speaker.getProximal(this.topic, noveltyAmount);
                }//end else if
                //SET_LANGUAGE command from Unity front-end.
                else if (split_input[0].Equals("SET_LANGUAGE"))
                {
                    //Index 1 is the new language mode.
                    language_mode = int.Parse(split_input[1]);
                    return "Language set to " + language_mode;
                }//end else if
            }//end else if

            // CASE: Nothing / Move on to next topic
            if (string.IsNullOrEmpty(input))
            {
                Feature nextTopic = this.topic;
                string[] newBuffer;
                
                // == testing forward projection
                if (false)
                {
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();
                    
                    int forwardTurn = 20;
                    List<Feature> testingForwardP = speaker.forwardProjection(nextTopic, forwardTurn);
                    
                    stopWatch.Stop();
                    TimeSpan ts = stopWatch.Elapsed;
                    // Format and display the TimeSpan value. 
                    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                        ts.Hours, ts.Minutes, ts.Seconds,
                        ts.Milliseconds / 10);
                    Console.WriteLine("RunTime of forward projection" + elapsedTime);
                    //print out all the topics
                    for (int i = 0; i < forwardTurn; i++)
                    {
                        Console.WriteLine(testingForwardP[i].Data);
                    }
                }
                
                // Can't guarantee it'll actually move on to anything...
                //If we are not projecting the current node as a topic, pick the next node normally
                if (!projectAsTopic)
                {
                    nextTopic = speaker.getNextTopic(nextTopic, "", this.turn);
                    //Console.WriteLine("Next Topic from " + this.topic.Data + " is " + nextTopic.Data);
                }//end if
                //If we are projecting the current node as a topic, pick the next node whose projected
                //path of nodes relate most to the current node (has the highest score).
                else
                {
                    Console.WriteLine("Current Topic: " + this.topic.Data);
                    //Go this many steps in the forward projection.
                    int forward_turn = 5;
                    //Get a list of all the neighbors to the current node
                    List<Tuple<Feature, double, string>> all_neighbors = this.topic.Neighbors;
                    //print out all neighbors
                    Console.WriteLine("Neighbors: ");
                    for (int i = 0; i < all_neighbors.Count; i++)
                    {
                        Console.WriteLine(all_neighbors[i].Item1.Data);
                    }//end for

                    //For each neighbor, find its projected path and sum the score of each node in the path relative to the current node.
                    double highest_score = -10000;
                    foreach (Tuple<Feature, double, string> neighbor_tuple in all_neighbors)
                    {
                        //First, check if the neighbor is a filtered node.
                        //If so, do not consider it.
                        if (filter_nodes.Contains(neighbor_tuple.Item1.Data))
                            continue;

                        List<Feature> projected_path = speaker.forwardProjection(neighbor_tuple.Item1, forward_turn);
                        //print out all the topics
                        /*Console.WriteLine("Projected Path: ");
                        for (int i = 0; i < forward_turn; i++)
                        {
                            Console.WriteLine(projected_path[i].Data);
                        }//end for*/

                        double total_score = 0;

                        //Total score calculation for topic
                        //Sum score of each path node relative to the current node
                        /*
                        foreach (Feature path_node in projected_path)
                        {
                            total_score += speaker.calculateScore(path_node, this.topic);
                        }//end foreach
                        //Console.WriteLine("Score for path: " + total_score);
                        */
                        //Total score calculation for joint mentions
                        //If a joint mention appears in the path, add an amount (currently just the joint mention weight)
                        //to the score of the neighbor (first path node) relative to the current node.
                        bool joint_mention_exists = true;
                        //For testing purposes, only check the first list in joint_mention_sets
                        foreach (Feature temp_node in joint_mention_sets[0])
                        {
                            if (!projected_path.Contains(temp_node))
                                joint_mention_exists = false;
                        }//end foreach
                        if (joint_mention_exists)
                        {
                            Console.WriteLine("Joint mention exists");
                            total_score = speaker.calculateScore(neighbor_tuple.Item1, this.topic) + this.graph.getSingleWeight(Constant.JointWeightIndex);
                        }//end if

                        if (total_score > highest_score)
                        {
                            highest_score = total_score;
                            nextTopic = neighbor_tuple.Item1;
                        }//end if
                    }//end foreach
                    //At the end of this foreach, nextTopic is set to the next node whose projected path had the highest sum score
                    //relative to the current node.
                    Console.WriteLine("Next Topic from " + this.topic.Data + " is " + nextTopic.Data + " with score " + highest_score);
                    Console.WriteLine("Path: ");
                    List<Feature> test_path = speaker.forwardProjection(nextTopic, forward_turn);
                    //print out all the topics
                    for (int i = 0; i < forward_turn; i++)
                    {
                        Console.WriteLine(test_path[i].Data);
                    }//end for
                }//end else

                /*
                //Check for filter nodes.
                if (filter_nodes.Contains(nextTopic.Data))
                {
                    //If it is a filter node, take another step.
                    Console.WriteLine("Filtering out " + nextTopic.Data);
                    ParseInput("", false, false);
                }//end if
                */
                
                noveltyInfo = speaker.getNovelty(nextTopic, this.turn, noveltyAmount);
                currentTopicNovelty = speaker.getCurrentTopicNovelty();
				noveltyValue = speaker.getCurrentTopicNovelty();
                newBuffer = FindStuffToSay(nextTopic);
                //MessageBox.Show("Explored " + nextTopic.Data + " with " + newBuffer.Length + " speaks.");

                nextTopic.DiscussedAmount += 1;
                this.graph.setFeatureDiscussedAmount(nextTopic.Data, nextTopic.DiscussedAmount);
                this.topic = nextTopic;
                // talk about
                this.buffer = newBuffer;
                answer = this.buffer[b++];
                if (projectAsTopic)
                    answer = "*****" + answer;
            }
            // CASE: Tell me more / Continue speaking
            else if (input.Contains("more") && input.Contains("tell"))
            {
                this.topic.DiscussedAmount += 1;
                this.graph.setFeatureDiscussedAmount(this.topic.Data, this.topic.DiscussedAmount);
                // talk about
                if (b < this.buffer.Length)
                    answer = this.buffer[b++];
                else
                {
                    if(language_mode == 0) { answer = "I've said all I can about that topic!"; }
                    else { answer = "我已经把我知道的都说完了。"; }
                }
                    
                noveltyInfo = speaker.getNovelty(this.topic, this.turn, noveltyAmount);
            }
            // CASE: New topic/question
            else
            {
                Query query = BuildQuery(input);
                if (query == null)
                {
                    return ParseInput("", messageToServer, false, true);
                    //answer = "I'm sorry, I'm afraid I don't understand what you are asking. But here's something I do know about. ";
                    //answer = answer + ParseInput("", false, false);
                    //out_of_topic = true;
                }
                else
                {
                    Feature feature = query.MainTopic;
                    feature.DiscussedAmount += 1;
                    this.graph.setFeatureDiscussedAmount(feature.Data, feature.DiscussedAmount);
                    this.topic = feature;
                    this.buffer = ParseQuery(query);
                    answer = this.buffer[b++];
                    noveltyInfo = speaker.getNovelty(this.topic, this.turn, noveltyAmount);
                }
            }

            //Update 
            updateHistory(this.topic);
            this.turn++;

            if (answer.Length == 0)
            {
                return IDK;
            }
            else
            {
                if (messageToServer)
                {
                    //Return message to Unity front-end with both novel and proximal nodes
                    return MessageToServer(this.topic, answer, noveltyInfo, speaker.getProximal(this.topic, noveltyAmount), forLog, outOfTopic);
                }

                if (outOfTopic)
                    answer += ParseInput("", false, false);

                if (forLog)
                    return answer;
                else
                    return answer;// +" <Novelty Info: " + noveltyInfo + " > <Proximal Info: " + speaker.getProximal(this.topic, noveltyAmount) + ">";
            }
        }

        /// <summary>
        /// Convert a regular string to a Query object,
        /// identifying the MainTopic and any question and direction words
        /// </summary>
        /// <param name="input">A string of input, asking about a topic</param>
        /// <returns>A Query object that can be passed to ParseQuery for output.</returns>
        public Query BuildQuery(string input)
        {
            string mainTopic;
            Question? questionType = null;
            Direction? directionType = null;

            // Find the main topic!
            Feature f = FindFeature(input);
            if (f == null)
            {
                //MessageBox.Show("FindFeature returned null for input: " + input);
                return null;
            }
            this.topic = f;
            mainTopic = this.topic.Data;
            if (string.IsNullOrEmpty(mainTopic))
            {
                //MessageBox.Show("mainTopic IsNullOrEmpty");
                return null;
            }

            // Is the input a question?
            if (input.Contains("where"))
            {
                questionType = Question.WHERE;
                if (input.Contains("was_hosted_at"))
                {
                    directionType = Direction.WAS_HOSTED_AT;
                }
            }
            else if (input.Contains("when"))
            {
                questionType = Question.WHEN;
            }
            else if (input.Contains("what") || input.Contains("?"))
            {
                questionType = Question.WHAT;
                // Check for direction words
				if (input.Contains("direction"))
				{
					foreach (string direction in directionWords)
					{
						// Ideally only one direction is specified
						if (input.Contains(direction))
                    	{
	                        directionType = (Direction)Enum.Parse(typeof(Direction), direction, true);
	                        // Don't break. If "northwest" is asked, "north" will match first
	                        // but then get replaced by "northwest" (and so on).
	                    }
					}
				}
            }
            else
            {
                int t = input.IndexOf("tell"), m = input.IndexOf("me"), a = input.IndexOf("about");
                if (0 <= t && t < m && m < a)
                {
                    // "Tell me about" in that order, with any words or so in between
                    // TODO:  Anything?  Should just talk about the topic, then.
                }
            }
            return new Query(this.graph.getFeature(mainTopic), questionType, directionType);
        }

        private string PadPunctuation(string s)
        {
            foreach (string p in punctuation)
            {
                s = s.Replace(p, " " + p);
            }
            return s;
        }
        private string RemovePunctuation(string s)
        {
            foreach (string p in punctuation)
            {
                s = s.Replace(p, "");
            }
            string[] s0 = s.Split(' ');
            return string.Join(" ", s0);
        }

        private Feature FindFeature(string input)
        {
            Feature target = null;
            int targetLen = 0;
            input = input.ToLower();
            foreach (string item in this.features)
            {
                string parse_item = item;
                parse_item = parse_item.Split(new string[] { "##" }, StringSplitOptions.None)[0];
                if (input.Contains(RemovePunctuation(parse_item.ToLower())))
                {
                    if (parse_item.Length > targetLen)
                    {
                        target = this.graph.getFeature(item);
                        targetLen = target.Data.Length;
                    }
                }
                /*
                // original
                if (input.Contains(RemovePunctuation(item.ToLower())))
                {
                    if (item.Length > targetLen)
                    {
                        target = this.graph.getFeature(item);
                        targetLen = target.Data.Length;
                    }
                }
                */
            }
            return target;
        }

        /// <summary>
        /// Takes a Query object and builds a list of output strings
        /// to talk about the query's MainTopic with its specified question
        /// words and direction words, if any, into consideration.
        /// </summary>
        /// <param name="query"></param>
        /// <returns>List of output strings.</returns>
        public string[] ParseQuery(Query query)
        {
            if (query == null)
                return new string[] { "I don't know." };

            List<string> output = new List<string>();

            if (query.IsQuestion)
            {
                switch (query.QuestionType)
                {
                    case Question.WHAT:
                        if (query.HasDirection)
                        {
                            // e.g. What is Direction of Topic?
                            // Find names of features that is DIRECTION of MainTopic
                            // Get list of <neighbor> tags
                            string dir = query.Direction.ToString().ToLower();
                            if (query.Direction == Direction.WON)
                            {
                                string[] neighbors = FindNeighborsByRelationship(query.MainTopic, dir);
                                // If the topic has no "won" links, then it is the event
                                if (neighbors.Length == 0)
                                {
                                    // So find the winner among its available neighbors
                                    neighbors = FindNeighborsByRelationship(query.MainTopic, "");
                                    foreach (string neighbor in neighbors)
                                    {
                                        // Look at ITS neighbors and see if there is a "won" whose name matches this one
                                        Feature nf = this.graph.getFeature(neighbor);
                                        foreach (var triple in nf.Neighbors)
                                        {
                                            if (triple.Item1.Data == query.MainTopic.Data && triple.Item3 == "won")
                                                output.Add(string.Format("{0} won {1}.", neighbor, query.MainTopic.Data));
                                        }
                                    }
                                }
                                // Otherwise it is the winner
                                else
                                {
                                    output.Add(string.Format("{0} won {1}.", query.MainTopic.Data, neighbors.ToList().JoinAnd()));
                                }
                            }
                            else if (query.Direction == Direction.HOSTED)
                            {
                                string[] neighbors = FindNeighborsByRelationship(query.MainTopic, dir);
                                if (neighbors.Length > 0)
                                    output.Add(string.Format("{0} hosted {1}.", query.MainTopic.Data, neighbors.ToList().JoinAnd()));
                            }
                            else
                            {
                                string[] neighbors = FindNeighborsByRelationship(query.MainTopic, dir);
                                if (neighbors.Length > 0)
                                    output.Add(string.Format("{0} of {1} {2} {3}", dir.ToUpperFirst(), query.MainTopic.Data,
                                        (neighbors.Length > 1) ? "are" : "is", neighbors.ToList().JoinAnd()));
                            }
                        }
                        else
                        {
                            // e.g. What is Topic?
                            // Get the <speak> attribute, if able
                            string[] speak = FindStuffToSay(query.MainTopic);
                            if (speak.Length > 0)
                                output.AddRange(speak);
                        }
                        break;
                    case Question.WHERE:
                        // e.g. "Where was Topic hosted at?"
                        if (query.HasDirection && query.Direction == Direction.WAS_HOSTED_AT)
                        {
                            string[] hostedAt = FindNeighborsByRelationship(query.MainTopic, query.Direction.ToString());
                            // Should only have one host, but treat it as an array
                            foreach (string host in hostedAt)
                                output.Add(query.MainTopic + " was hosted at " + host + ".");
                        }
                        else
                        {
                            // e.g. Where is Topic?
                            // Get all the neighbors from this feature and the "opposite" directions
                            output.AddRange((SpeakNeighborRelations(query.MainTopic.Data, FindAllNeighbors(query.MainTopic))));
                        }
                        break;
                    case Question.WHEN:
                        // e.g. When was Topic made/built/etc.?
                        break;
                }
            }
            else
            {
                // e.g.:
                // Tell me about Topic.
                // Topic.
                output.AddRange(FindStuffToSay(query.MainTopic));
            }

            return output.Count() > 0 ? output.ToArray() : new string[] { IDK };
        }

        private string[] FindSpeak(Feature feature)
        {
            return feature.Speaks.ToArray();
        }

        private string[] FindStuffToSay(Feature feature)
        {
            List<string> stuff = new List<string>();
            string[] speaks = FindSpeak(feature);
            if (speaks.Length > 0)
            {
                // parse output according to language mode
                if (speaks[0].Contains("##"))
                {
                    if (language_mode == 0)
                    {
                        speaks[0] = speaks[0].Split(new string[] { "##" }, StringSplitOptions.None)[0];
                    }
                    else
                    {
                        speaks[0] = speaks[0].Split(new string[] { "##" }, StringSplitOptions.None)[1];
                    }
                }
                stuff.AddRange(speaks);
            }
                
            stuff.AddRange(SpeakNeighborRelations(feature.Data, FindAllNeighbors(feature)));
            if (stuff.Count() == 0)
            {
                stuff.Add(feature.Data);
            }
            return stuff.ToArray();
        }

        private string[] FindNeighborsByRelationship(Feature feature, string relationship)
        {
            List<string> neighborNames = new List<string>();
            var neighbors = feature.Neighbors;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var triple = neighbors[i];
                Feature neighbor = triple.Item1;
                string relation = triple.Item3;
                if (relation.ToLower().Replace(' ', '_') == relationship.ToLower())
                    neighborNames.Add(neighbor.Data);
            }
            return neighborNames.ToArray();
        }

        private Tuple<string, Direction>[] FindAllNeighbors(Feature feature)
        {
            var _neighbors = feature.Neighbors;
            var neighbors = new List<Tuple<string, Direction>>();
            foreach (var triple in _neighbors)
            {
                string neighborName = triple.Item1.Data;
                string relationship = triple.Item3;
                if (directionWords.Contains(relationship))
                    neighbors.Add(new Tuple<string, Direction>(neighborName,
                        ((Direction)Enum.Parse(typeof(Direction), relationship.ToUpper().Replace(' ', '_')))));
            }
            return neighbors.ToArray();
        }

        private string[] SpeakNeighborRelations(string featureName, Tuple<string, Direction>[] neighbors)
        {
            string[] neighborRelations = new string[neighbors.Length];
            if (neighborRelations.Length == 0)
                return new string[] { };
            for (int i = 0; i < neighborRelations.Length; i++)
                neighborRelations[i] = string.Format("{0} is {1} of {2}.",
                    (i == 0) ? featureName : "It",
                    neighbors[i].Item2.Invert().ToString().ToLower(),
                    neighbors[i].Item1);
            return neighborRelations;
        }
    }

    static class ExtensionMethods
    {
        public static Direction Invert(this Direction d)
        {
            return (Direction)(-(int)d);
        }

        public static string ToUpperFirst(this string s)
        {
            return s.Substring(0, 1).ToUpper() + s.Substring(1);
        }

        public static string JoinAnd(this List<string> items)
        {
            switch (items.Count())
            {
                case 0:
                    return "";
                case 1:
                    return items.ElementAt(0);
                case 2:
                    return items.ElementAt(0) + " and " + items.ElementAt(1);
                default:
                    return string.Join(", ", items.GetRange(0, items.Count - 1))
                        + ", and " + items[items.Count - 1];
            }
        }
    }
}
