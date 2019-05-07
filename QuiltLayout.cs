using System;
using System.Collections.Generic;
using CoreGraphics;
using Foundation;
using UIKit;

namespace QuiltLayout
{
    public class QuiltLayout : UICollectionViewLayout
    {
        private CGPoint firstOpenSpace;
        private CGPoint furthestBlockPoint;
        private Dictionary<int, Dictionary<int, NSIndexPath>> indexPathByPosition;
        private Dictionary<int, Dictionary<int, CGPoint>> positionByIndexPath;
        private bool hasPositionsCached;
        private UICollectionViewLayoutAttributes[] previousLayoutAttributes;
        private CGRect previousLayoutRect;
        private NSIndexPath lastIndexPathPlaced;

        public QuiltLayoutDelegate Delegate { get; set; }
        public CGSize BlockPixels { get; set; }
        public UICollectionViewScrollDirection Direction { get; set; }
        public bool PrelayoutEverything { get; set; }

        public override CGSize CollectionViewContentSize
        {
            get
            {
                bool isVert = this.Direction == UICollectionViewScrollDirection.Vertical;
                var contentRect = CollectionView.ContentInset.InsetRect(CollectionView.Frame);
                if (isVert)
                    return new CGSize(contentRect.Width, (furthestBlockPoint.Y + 1) * BlockPixels.Height);
                else
                    return new CGSize((furthestBlockPoint.X + 1) * BlockPixels.Width, contentRect.Height);
            }
        }

        public QuiltLayout()
        {
            BlockPixels = new CGSize(100, 100);
            Direction = UICollectionViewScrollDirection.Vertical;
        }

        public override UICollectionViewLayoutAttributes[] LayoutAttributesForElementsInRect(CGRect rect)
        {
            if (Delegate == null) return new UICollectionViewLayoutAttributes[] { };

            if (rect.Equals(previousLayoutRect)) return previousLayoutAttributes;

            previousLayoutRect = rect;

            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;

            int unrestrictedDimensionStart = (int)(isVert ? rect.Y / BlockPixels.Height : rect.X / BlockPixels.Width);
            int unrestrictedDimensionLength = (int)((isVert ? rect.Size.Height / BlockPixels.Height : rect.Size.Width / BlockPixels.Width) + 1);
            int unrestrictedDimensionEnd = unrestrictedDimensionStart + unrestrictedDimensionLength;

            FillInBlocksToUnrestrictedRow(PrelayoutEverything ? int.MaxValue : unrestrictedDimensionEnd);

            var attributes = new NSMutableSet();

            TraverseTilesBetweenUnrestrictedDimension(unrestrictedDimensionStart, unrestrictedDimensionEnd, (CGPoint point) =>
            {
                NSIndexPath indexPath = IndexPathForPosition(point);
                if (indexPath != null)
                {
                    attributes.Add(LayoutAttributesForItem(indexPath));
                }
                return true;
            });

            return previousLayoutAttributes = attributes.ToArray<UICollectionViewLayoutAttributes>();
        }

        public override UICollectionViewLayoutAttributes LayoutAttributesForItem(NSIndexPath indexPath)
        {
            var insets = UIEdgeInsets.Zero;

            if (Delegate.RespondsToSelector(new ObjCRuntime.Selector("collectionView:layout:insetsForItemAtIndexPath:")))
                insets = Delegate.InsetForItem(CollectionView, this, indexPath);
                
            CGRect frame = FrameForIndexPath(indexPath);

            var attributes = UICollectionViewLayoutAttributes.CreateForCell(indexPath);
            attributes.Frame = insets.InsetRect(frame);

            return attributes;
        }

        public override bool ShouldInvalidateLayoutForBoundsChange(CGRect newBounds)
        {
            return !(newBounds.Size.Equals(CollectionView.Frame.Size));
        }

        public override void PrepareForCollectionViewUpdates(UICollectionViewUpdateItem[] updateItems)
        {
            base.PrepareForCollectionViewUpdates(updateItems);

            foreach (var item in updateItems)
            {
                if (item.UpdateAction == UICollectionUpdateAction.Insert || item.UpdateAction == UICollectionUpdateAction.Move)
                {
                    FillInBlocksToIndexPath(item.IndexPathAfterUpdate);
                }
            }
        }

        public override void InvalidateLayout()
        {
            base.InvalidateLayout();

            furthestBlockPoint = CGPoint.Empty;
            firstOpenSpace = CGPoint.Empty;
            previousLayoutRect = CGRect.Empty;
            previousLayoutAttributes = null;
            lastIndexPathPlaced = null;
            clearPositions();
        }

        public override void PrepareLayout()
        {
            base.PrepareLayout();

            if (Delegate == null) return;

            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;

            var scrollFrame = new CGRect(CollectionView.ContentOffset.X, CollectionView.ContentOffset.Y, CollectionView.Frame.Size.Width, CollectionView.Frame.Size.Height);

            int unrestrictedRow = 0;
            if (isVert)
                unrestrictedRow = (int)((scrollFrame.GetMaxY() / BlockPixels.Height) + 1);
            else
                unrestrictedRow = (int)((scrollFrame.GetMaxX() / BlockPixels.Width) + 1);
                
            FillInBlocksToUnrestrictedRow(PrelayoutEverything ? int.MaxValue : unrestrictedRow);
        }

        public void SetDirection(UICollectionViewScrollDirection direction)
        {
            Direction = direction;
            InvalidateLayout();
        }

        public void SetBlockPixels(CGSize size)
        {
            BlockPixels = size;
            InvalidateLayout();
        }

        #region Private Methods
        private void FillInBlocksToUnrestrictedRow(int endRow)
        {
            bool vert = Direction == UICollectionViewScrollDirection.Vertical;

            int numSections = (int)CollectionView.NumberOfSections();
            for (int section = (lastIndexPathPlaced == null ? 0 : lastIndexPathPlaced.Section); section < numSections; section++)
            {
                int numRows = (int)CollectionView.NumberOfItemsInSection(section);
                for (int row = (lastIndexPathPlaced == null ? 0 : lastIndexPathPlaced.Row + 1); row < numRows; row++)
                {
                    NSIndexPath indexPath = NSIndexPath.FromRowSection(row, section);

                    if (PlaceBlockAtIndex(indexPath))
                    {
                        lastIndexPathPlaced = indexPath;
                    }

                    // only jump out if we've already filled up every space up till the resticted row
                    if ((vert ? firstOpenSpace.Y : firstOpenSpace.X) >= endRow)
                    {
                        return;
                    }
                }
            }
        }

        private void FillInBlocksToIndexPath(NSIndexPath path)
        {
            var numSections = CollectionView.NumberOfSections();
            for (int section = (lastIndexPathPlaced == null ? 0 : lastIndexPathPlaced.Section); section < numSections; section++)
            {
                int numRows = (int)CollectionView.NumberOfItemsInSection(section);
                for (int row = lastIndexPathPlaced == null ? 0 : lastIndexPathPlaced.Row + 1 ; row < numRows; row++)
                {
                    if (section >= path.Section && row > path.Row)
                    {
                        return;
                    }

                    NSIndexPath indexPath = NSIndexPath.FromRowSection(row, section);
                    if (PlaceBlockAtIndex(indexPath))
                    {
                        lastIndexPathPlaced = indexPath;
                    }
                }
            }
        }

        private bool PlaceBlockAtIndex(NSIndexPath indexPath)
        {
            CGSize blockSize = GetBlockSizeForItemAtIndexPath(indexPath);
            bool vert = Direction == UICollectionViewScrollDirection.Vertical;

            bool traverseOpenTiles = TraverseOpenTiles((CGPoint blockOrigin) =>
            {
                // we need to make sure each square in the desired
                // area is available before we can place the square

                bool didTraverseAllBlocks = this.TraverseTilesForPoint(blockOrigin, blockSize, (CGPoint point) =>
                {
                    bool spaceAvailable = !(this.IndexPathForPosition(point) != null);
                    bool inBounds = (vert ? point.X : point.Y) < RestrictedDimensionBlockSize();
                    bool maximumRestrictedBoundSize = (vert ? blockOrigin.X : blockOrigin.Y) == 0;

                    if (spaceAvailable && maximumRestrictedBoundSize && !inBounds)
                    {
                        return true;
                    }

                    return spaceAvailable && inBounds;
                });

                if (!didTraverseAllBlocks)
                {
                    return true;
                }

                SetIndexPath(indexPath, blockOrigin);

                TraverseTilesForPoint(blockOrigin, blockSize, (CGPoint point) =>
                {
                    SetPosition(point, indexPath);
                    furthestBlockPoint = point;
                    return true; 
                });
                return false;
            });

            return !traverseOpenTiles;
        }

        // returning no in the callback will
        // terminate the iterations early
        private bool TraverseTilesBetweenUnrestrictedDimension(int begin, int end, Func<CGPoint, bool> block)
        {
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;
            for (int unrestrictedDimension = begin; unrestrictedDimension < end; unrestrictedDimension++)
            {
                for (int restrictedDimension = 0; restrictedDimension < RestrictedDimensionBlockSize(); restrictedDimension++)
                {
                    CGPoint point = new CGPoint(isVert ? restrictedDimension : unrestrictedDimension, isVert ? unrestrictedDimension : restrictedDimension);
                    if (!block(point))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // returning no in the callback will
        // terminate the iterations early
        private bool TraverseTilesForPoint(CGPoint point, CGSize size, Func<CGPoint, bool> block)
        {
            for (int col = (int)point.X; col < point.X+size.Width; col++)
            {
                for (int row = (int)point.Y; row < point.Y + size.Height; row++)
                {
                    if (!block(new CGPoint(col, row)))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // returning no in the callback will
        // terminate the iterations early
        private bool TraverseOpenTiles(Func<CGPoint, bool> block)
        {
            bool allTakenBefore = true;
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;

            // the double ;; is deliberate, the unrestricted dimension should iterate indefinitely
            for (int unrestrictedDimension = ((int)(isVert ? firstOpenSpace.Y : firstOpenSpace.X));; unrestrictedDimension++)
            {
                for (int restrictedDimension = 0; restrictedDimension < RestrictedDimensionBlockSize(); restrictedDimension++)
                {
                    CGPoint point = new CGPoint(isVert ? restrictedDimension : unrestrictedDimension, isVert ? unrestrictedDimension : restrictedDimension);

                    if (IndexPathForPosition(point) != null) { continue; }

                    if (allTakenBefore)
                    {
                        firstOpenSpace = point;
                        allTakenBefore = false;
                    }

                    if (!block(point))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void clearPositions()
        {
            indexPathByPosition = new Dictionary<int, Dictionary<int, NSIndexPath>>();
            positionByIndexPath = new Dictionary<int, Dictionary<int, CGPoint>>();
        }

        private NSIndexPath IndexPathForPosition(CGPoint point)
        {
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;

            int unrestrictedPoint = (int)(isVert ? point.Y : point.X);
            int restrictedPoint = (int)(isVert ? point.X : point.Y);

            if (indexPathByPosition.ContainsKey(restrictedPoint) && indexPathByPosition[restrictedPoint].ContainsKey(unrestrictedPoint))
            {
                return indexPathByPosition[restrictedPoint][unrestrictedPoint];
            }
            return null;
        }

        private void SetPosition(CGPoint point, NSIndexPath indexPath)
        {
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;

            int unrestrictedPoint = (int)(isVert ? point.Y : point.X);
            int restrictedPoint = (int)(isVert ? point.X : point.Y);

            var innerDict = indexPathByPosition.ContainsKey(restrictedPoint) ? indexPathByPosition[restrictedPoint] : null;
            if (innerDict == null)
            {
                indexPathByPosition[restrictedPoint] = new Dictionary<int, NSIndexPath>();
            }

            indexPathByPosition[restrictedPoint][unrestrictedPoint] = indexPath;
        }

        private void SetIndexPath(NSIndexPath path, CGPoint point)
        {
            var innerDict = positionByIndexPath.ContainsKey(path.Section) ? positionByIndexPath[path.Section] : null;
            if (innerDict == null)
            {
                positionByIndexPath[path.Section] = new Dictionary<int, CGPoint>();
            }

            positionByIndexPath[path.Section][path.Row] = point;
        }

        private CGPoint PositionForIndexPath(NSIndexPath path)
        {
            // if item does not have a position, we will make one!
            if (positionByIndexPath[path.Section][path.Row] == null)
            {
                FillInBlocksToIndexPath(path);
            }

            return positionByIndexPath[path.Section][path.Row];
        }

        private CGRect FrameForIndexPath(NSIndexPath path)
        {
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;
            CGPoint position = PositionForIndexPath(path);
            CGSize elementSize = GetBlockSizeForItemAtIndexPath(path);

            CGRect contentRect = CollectionView.ContentInset.InsetRect(CollectionView.Frame);
            if (isVert)
            {
                float initialPaddingForContraintedDimension = (float)((contentRect.Width - RestrictedDimensionBlockSize() * BlockPixels.Width) / 2);
                return new CGRect(
                    position.X * BlockPixels.Width + initialPaddingForContraintedDimension,
                    position.Y * BlockPixels.Height,
                    elementSize.Width * BlockPixels.Width,
                    elementSize.Height * BlockPixels.Height
                );
            }
            else
            {
                float initialPaddingForContraintedDimension = (float)((contentRect.Height - RestrictedDimensionBlockSize() * BlockPixels.Height) / 2);
                return new CGRect(
                        position.X * BlockPixels.Width,
                        position.Y * BlockPixels.Height + initialPaddingForContraintedDimension,
                        elementSize.Width * BlockPixels.Width,
                        elementSize.Height * BlockPixels.Height
                    );
            }
        }

        //This method is prefixed with get because it may return its value indirectly
        private CGSize GetBlockSizeForItemAtIndexPath(NSIndexPath indexPath)
        {
            var blockSize = new CGSize(1, 1);
            if (Delegate.RespondsToSelector(new ObjCRuntime.Selector("collectionView:layout:blockSizeForItemAtIndexPath:")))
                blockSize = Delegate.BlockSizeForItem(CollectionView, this, indexPath);
            return blockSize;
        }

        // this will return the maximum width or height the quilt
        // layout can take, depending on we're growing horizontally
        // or vertically
        private int RestrictedDimensionBlockSize()
        {
            bool isVert = Direction == UICollectionViewScrollDirection.Vertical;
            CGRect contentRect = CollectionView.ContentInset.InsetRect(CollectionView.Frame);
            int size = (int)(isVert ? contentRect.Width / BlockPixels.Width : contentRect.Height / BlockPixels.Height);
            if (size == 0)
            {
                Console.WriteLine($"Cannot fit block of size {BlockPixels.ToString()} in content rect {contentRect.ToString()}");
                return 1;
            }
            return size;
        }

        private void SetFurthestBlockPoint(CGPoint point)
        {
            furthestBlockPoint = new CGPoint((nfloat)Math.Max(furthestBlockPoint.X, point.X), Math.Max(furthestBlockPoint.Y, point.Y));
        }
        #endregion
    }
}