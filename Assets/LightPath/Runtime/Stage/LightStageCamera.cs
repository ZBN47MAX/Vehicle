using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 四视角机位、联合边界取景和透视正交过渡。 */
    public sealed partial class LightStage
    {
        /** @brief 过渡起点位置。 */
        private Vector3 _fromPosition;
        /** @brief 过渡起点朝向。 */
        private Quaternion _fromRotation;
        /** @brief 过渡起点投影。 */
        private Matrix4x4 _fromProjection;
        /** @brief 过渡目标位置。 */
        private Vector3 _toPosition;
        /** @brief 过渡目标朝向。 */
        private Quaternion _toRotation;
        /** @brief 已用过渡时间。 */
        private float _elapsed;
        /** @brief 过渡是否尚未完成。 */
        public bool Moving { get; private set; }
        /** @brief 目标正交半高。 */
        private float _halfHeight;
        /** @brief 当前屏幕宽高比缓存，用于窗口缩放后重新取景。 */
        private float _aspect;
        /** @brief 切换状态并计算取景。@param view 目标状态 @param snap 是否直接定位 */
        public void SetView(StageView view, bool snap = false)
        {
            View = view;
            _fromPosition = _viewCamera.transform.position;
            _fromRotation = _viewCamera.transform.rotation;
            float observedAspect = _viewCamera.aspect;
            _aspect = LightStageMath.ResolveAspect(observedAspect, _aspect);
            _fromProjection = LightStageMath.ValidAspect(observedAspect)
                ? _viewCamera.projectionMatrix
                : Matrix4x4.Perspective(_config.FieldOfView, _aspect, _viewCamera.nearClipPlane, _viewCamera.farClipPlane);
            if (view == StageView.World || view == StageView.Repair)
            {
                Transform frame = view == StageView.World ? _screenFrame : _repairFrame;
                Vector2 size = view == StageView.World ? _config.ScreenSize : _config.RepairSize;
                _toRotation = frame.rotation;
                _toPosition = frame.position - frame.forward * (view == StageView.World ? _config.ScreenDistance : _config.RepairDistance);
                _halfHeight = Mathf.Max(size.y, size.x / _aspect) / 2 * (view == StageView.World ? 1 : _config.FramePadding);
            }
            else
            {
                Transform pose = view == StageView.Overview ? _overviewPose : _projectionPose;
                _toPosition = pose.position;
                _toRotation = pose.rotation;
                if (view == StageView.Overview)
                    AddPlanePoints(_repairFrame, _config.RepairSize);
                FitPerspective();
            }
            _elapsed = snap ? _config.TransitionSeconds : 0;
            Moving = !snap;
            ApplyCamera(0);
        }
        /** @brief 沿固定机位方向后退，包住屏幕与全部实际物件边界。 */
        private void FitPerspective()
        {
            float retreat = 0;
            Quaternion inverseRotation = Quaternion.Inverse(_toRotation);
            foreach (Vector3 point in _framePoints)
            {
                Vector3 local = inverseRotation * (point - _toPosition);
                local.x *= _config.FramePadding; local.y *= _config.FramePadding;
                retreat = Mathf.Max(retreat, LightStageMath.Retreat(local, _config.FieldOfView, _aspect, _viewCamera.nearClipPlane + .1f));
            }
            _toPosition -= _toRotation * Vector3.forward * retreat;
        }
        /** @brief 推进镜头；暂停时传零。投影调整只向后扩展取景，不因选择物件来回跳转。@param seconds 无缩放秒数 */
        public void Animate(float seconds)
        {
            if (!Ready)
                return;
            float aspect = _viewCamera.aspect;
            if (LightStageMath.ValidAspect(aspect) && !Mathf.Approximately(_aspect, aspect))
            {
                SetView(View, true);
                return;
            }
            if (!Moving && View != StageView.Projection) return;
            ApplyCamera(seconds);
        }
        /** @brief 提交相机过渡；不回调SetView，避免窗口尺寸变化时形成递归。@param seconds 无缩放秒数 */
        private void ApplyCamera(float seconds)
        {
            if (!Moving && View == StageView.Projection)
                FitPerspective();
            _elapsed = Mathf.Min(_config.TransitionSeconds, _elapsed + seconds);
            float t = Mathf.SmoothStep(0, 1, _elapsed / _config.TransitionSeconds);
            Vector3 position = !Moving && View == StageView.Projection && seconds > 0
                ? Vector3.Lerp(_viewCamera.transform.position, _toPosition, 1 - Mathf.Exp(-8 * seconds))
                : Vector3.Lerp(_fromPosition, _toPosition, t);
            _viewCamera.transform.SetPositionAndRotation(position, Quaternion.Slerp(_fromRotation, _toRotation, t));
            bool ortho = View == StageView.World || View == StageView.Repair;
            Matrix4x4 target = ortho
                ? Matrix4x4.Ortho(-_halfHeight * _aspect, _halfHeight * _aspect, -_halfHeight, _halfHeight, _viewCamera.nearClipPlane, _viewCamera.farClipPlane)
                : Matrix4x4.Perspective(_config.FieldOfView, _aspect, _viewCamera.nearClipPlane, _viewCamera.farClipPlane);
            Matrix4x4 blended = target;
            if (t < 1)
                for (int i = 0; i < 16; i++) blended[i] = Mathf.Lerp(_fromProjection[i], target[i], t);
            _viewCamera.projectionMatrix = blended;
            Moving = _elapsed < _config.TransitionSeconds;
            _viewCamera.cullingMask = (View == StageView.World && !Moving ? 0 : 1 << _config.DisplayLayer) | (1 << _config.ScreenLayer);
            if (!Moving)
            {
                _viewCamera.orthographic = ortho;
                _viewCamera.orthographicSize = Mathf.Max(.01f, _halfHeight);
                _viewCamera.fieldOfView = _config.FieldOfView;
                if (LightStageMath.ValidAspect(_viewCamera.aspect))
                    _viewCamera.ResetProjectionMatrix();
            }
        }
    }
}
