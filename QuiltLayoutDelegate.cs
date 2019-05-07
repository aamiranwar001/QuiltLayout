using CoreGraphics;
using Foundation;
using UIKit;

namespace QuiltLayout
{
    public class QuiltLayoutDelegate : UICollectionViewDelegate
    {
        [Export("collectionView:layout:blockSizeForItemAtIndexPath:")]
        public virtual CGSize BlockSizeForItem(UICollectionView collectionView, UICollectionViewLayout collectionViewLayout, NSIndexPath indexPath)
        {
            return new CGSize(1, 1);
        }

        [Export("collectionView:layout:insetsForItemAtIndexPath:")]
        public virtual UIEdgeInsets InsetForItem(UICollectionView collectionView, UICollectionViewLayout collectionViewLayout, NSIndexPath indexPath)
        {
            return UIEdgeInsets.Zero;
        }
    }
}
