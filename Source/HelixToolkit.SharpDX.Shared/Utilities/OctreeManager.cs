﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Windows;
using SharpDX;
using System.Diagnostics;
using HelixToolkit.SharpDX.Shared.Utilities;
using HelixToolkit.SharpDX.Shared.Model;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Markup;

namespace HelixToolkit.Wpf.SharpDX
{
    public abstract class OctreeManagerBase : FrameworkElement, IOctreeManager
    {
        public static readonly DependencyProperty OctreeProperty
            = DependencyProperty.Register("Octree", typeof(IOctree), typeof(OctreeManagerBase),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MinSizeProperty
            = DependencyProperty.Register("MinSize", typeof(float), typeof(OctreeManagerBase),
                new PropertyMetadata(1f, (s, e) => { (s as OctreeManagerBase).Parameter.MinSize = (float)e.NewValue; }));

        public static readonly DependencyProperty AutoDeleteIfEmptyProperty
            = DependencyProperty.Register("AutoDeleteIfEmpty", typeof(bool), typeof(OctreeManagerBase),
                new PropertyMetadata(true, (s, e) => { (s as OctreeManagerBase).Parameter.AutoDeleteIfEmpty = (bool)e.NewValue; }));

        public static readonly DependencyProperty CubifyPropertyProperty
            = DependencyProperty.Register("Cubify", typeof(bool), typeof(OctreeManagerBase),
                new PropertyMetadata(false, (s, e) => { (s as OctreeManagerBase).Parameter.Cubify = (bool)e.NewValue; }));

        public static readonly DependencyProperty RecordHitPathBoundingBoxesProperty
            = DependencyProperty.Register("RecordHitPathBoundingBoxes", typeof(bool), typeof(OctreeManagerBase),
                new PropertyMetadata(false, (s, e) => { (s as OctreeManagerBase).Parameter.RecordHitPathBoundingBoxes = (bool)e.NewValue; }));

        public IOctree Octree
        {
            set
            {
                SetValue(OctreeProperty, value);
            }
            get
            {
                return (IOctree)GetValue(OctreeProperty);
            }
        }

        /// <summary>
        /// Minimum octant size
        /// </summary>
        public float MinSize
        {
            set
            {
                SetValue(MinSizeProperty, value);
            }
            get
            {
                return (float)GetValue(MinSizeProperty);
            }
        }
        /// <summary>
        /// Delete octant node if its empty
        /// </summary>
        public bool AutoDeleteIfEmpty
        {
            set
            {
                SetValue(AutoDeleteIfEmptyProperty, value);
            }
            get
            {
                return (bool)GetValue(AutoDeleteIfEmptyProperty);
            }
        }
        /// <summary>
        /// Create cube octree
        /// </summary>
        public bool Cubify
        {
            set
            {
                SetValue(CubifyPropertyProperty, value);
            }
            get
            {
                return (bool)GetValue(CubifyPropertyProperty);
            }
        }
        /// <summary>
        /// Record the hit path bounding box for debugging
        /// </summary>
        public bool RecordHitPathBoundingBoxes
        {
            set
            {
                SetValue(RecordHitPathBoundingBoxesProperty, value);
            }
            get
            {
                return (bool)GetValue(RecordHitPathBoundingBoxesProperty);
            }
        }

        private GeometryModel3DOctree mOctree = null;

        public OctreeBuildParameter Parameter { private set; get; } = new OctreeBuildParameter();

        private bool mEnabled = true;
        public bool Enabled
        {
            set
            {
                mEnabled = value;
                if (!mEnabled)
                {
                    Clear();
                }
            }
            get
            {
                return mEnabled;
            }
        }

        public bool RequestUpdateOctree { get { return mRequestUpdateOctree; } protected set { mRequestUpdateOctree = value; } }
        private volatile bool mRequestUpdateOctree = false;

        public abstract bool AddPendingItem(Element3D item);

        public abstract void Clear();

        public abstract void RebuildTree(IList<Element3D> items);

        public abstract void RemoveItem(Element3D item);

        public abstract void RequestRebuild();
    }
    /// <summary>
    /// Use to create geometryModel3D octree for groups. Each ItemsModel3D must has its own manager, do not share between two ItemsModel3D
    /// </summary>
    public sealed class GeometryModel3DOctreeManager : OctreeManagerBase
    {
        private GeometryModel3DOctree mOctree = null;

        public GeometryModel3DOctreeManager()
        {
        }

        private void UpdateOctree(GeometryModel3DOctree tree)
        {
            Octree = tree;
            mOctree = tree;
        }

        public override void RebuildTree(IList<Element3D> items)
        {
            RequestUpdateOctree = false;
            if (Enabled)
            {
                UpdateOctree(RebuildOctree(items));
                if (Octree == null)
                {
                    RequestRebuild();
                }
            }
            else
            {
                Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SubscribeBoundChangeEvent(GeometryModel3D item)
        {
            item.OnBoundChanged -= Item_OnBoundChanged;
            item.OnBoundChanged += Item_OnBoundChanged;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UnsubscribeBoundChangeEvent(GeometryModel3D item)
        {
            item.OnBoundChanged -= Item_OnBoundChanged;
        }

        private void Item_OnBoundChanged(object sender, BoundChangedEventArgs e)
        {
            var item = sender as GeometryModel3D;
            if (Octree == null || !item.IsAttached)
            {
                UnsubscribeBoundChangeEvent(item);
                return;
            }
            var arg = e;
            int index;
            var node = mOctree.FindChildByItemBound(item, arg.OldBound, out index);
            bool rootAdd = true;
            if (node != null)
            {
                var tree = mOctree;
                UpdateOctree(null);
                var geoNode = node as GeometryModel3DOctree;
                if (geoNode.Bound.Contains(arg.NewBound) == ContainmentType.Contains)
                {
                    Debug.WriteLine("new bound inside current node");
                    if (geoNode.Add(item))
                    {
                        geoNode.RemoveAt(index); //remove old item from node after adding successfully.
                        rootAdd = false;
                    }
                }
                else
                {
                    geoNode.RemoveAt(index);
                    Debug.WriteLine("new bound outside current node");
                }
                UpdateOctree(tree);
            }
            if (rootAdd)
            {
                AddItem(item);
            }
        }

        private GeometryModel3DOctree RebuildOctree(IList<Element3D> items)
        {
            Clear();
            if (items == null || items.Count == 0)
            {
                return null;
            }
            var list = items.Where(x => x is GeometryModel3D).Select(x => x as GeometryModel3D).ToList();
            var tree = new GeometryModel3DOctree(list, Parameter);
            tree.BuildTree();
            if (tree.TreeBuilt)
            {
                foreach (var item in list)
                {
                    SubscribeBoundChangeEvent(item);
                }
            }
            return tree.TreeBuilt ? tree : null;
        }

        private static readonly BoundingBox ZeroBound = new BoundingBox();
        public override bool AddPendingItem(Element3D item)
        {
            if (Enabled && item is GeometryModel3D)
            {
                var model = item as GeometryModel3D;
                model.OnBoundChanged -= GeometryModel3DOctreeManager_OnBoundInitialized;
                model.OnBoundChanged += GeometryModel3DOctreeManager_OnBoundInitialized;
                if (model.Bounds != ZeroBound)
                {
                    AddItem(model);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private void GeometryModel3DOctreeManager_OnBoundInitialized(object sender, BoundChangedEventArgs e)
        {
            var item = sender as GeometryModel3D;
            item.OnBoundChanged -= GeometryModel3DOctreeManager_OnBoundInitialized;
            AddItem(item);
        }

        private void AddItem(Element3D item)
        {
            if (Enabled && item is GeometryModel3D)
            {
                var tree = mOctree;
                UpdateOctree(null);
                var model = item as GeometryModel3D;
                if (tree == null || !tree.Add(model))
                {
                    RequestRebuild();
                }
                else
                {
                    UpdateOctree(tree);
                }
                SubscribeBoundChangeEvent(model);
            }
        }

        public override void RemoveItem(Element3D item)
        {
            if (Enabled && Octree != null && item is GeometryModel3D)
            {
                var tree = mOctree;
                UpdateOctree(null);
                var model = item as GeometryModel3D;
                model.OnBoundChanged -= GeometryModel3DOctreeManager_OnBoundInitialized;
                UnsubscribeBoundChangeEvent(model);
                if (!tree.RemoveByBound(model))
                {
                    Console.WriteLine("Remove failed.");
                }
                UpdateOctree(tree);
            }
        }

        public override void Clear()
        {
            RequestUpdateOctree = false;
            UpdateOctree(null);
        }

        public override void RequestRebuild()
        {
            Clear();
            RequestUpdateOctree = true;
        }
    }
}
