﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using LostManuscriptII;

namespace Lost_Manuscript_II_Data_Entry
{
    public partial class TreeFrom : Form
    {
        private bool flag;
        private bool canDisplay;
        private FeatureGraph featGraph;

        public TreeFrom(FeatureGraph featGraph, bool flag)
        {
            this.canDisplay = true;
            this.featGraph = featGraph;
            this.flag = flag;
            InitializeComponent();
            refreshTreeView();
        }

        public void refreshTreeView()
        {
            treeView1.Nodes.Clear();
            if (flag)
            {
                List<Feature> tmp = featGraph.Features;
                treeDrillFill(treeView1, tmp);
            }
            else
            {
                if (featGraph.Root == null)
                {
                    MessageBox.Show("There is no root set.\nYou need to set a root to view the tree trunk.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    this.canDisplay = false;
                    return;
                }
                treeDrillFillHelper(treeView1.Nodes.Add(featGraph.Root.Data), featGraph.Root);
            }
            treeView1.Refresh();
        }

        private void treeDrillFill(TreeView toRefresh, List<Feature> toFill)
        {
            if (toFill.Count == 0)
            {
                toRefresh.Nodes.Add("EMPTY FEATURE GRAPH");
                return;
            }
            for (int x = 0; x < toFill.Count; x++)
            {
                treeDrillFillHelper(toRefresh.Nodes.Add(toFill[x].Data), toFill[x]);
            }
        }
        private void treeDrillFillHelper(TreeNode toRefresh, Feature toFill)
        {
            for (int x = 0; x < toFill.Neighbors.Count; x++)
            {
                if (toRefresh.Parent == null || toRefresh.Parent.Text != toFill.Neighbors[x].Item1.Data)
                {
                    treeDrillFillHelper(toRefresh.Nodes.Add(toFill.Neighbors[x].Item1.Data), toFill.Neighbors[x].Item1);
                }
                else
                {
                    toRefresh.Nodes.Add(toFill.Neighbors[x].Item1.Data + "... (Infinite Relation)");
                }
            }
            return;
        }
        private TreeNode getTreeNode(TreeView toSearch, string data)
        {
            for (int x = 0; x < toSearch.Nodes.Count; x++)
            {
                if (toSearch.Nodes[x].Text == data) { return toSearch.Nodes[x]; }
            }
            return null;
        }
        public bool CanDisplay
        {
            get
            {
                return this.canDisplay;
            }
        }
    }
}
