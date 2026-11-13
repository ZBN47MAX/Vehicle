using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 将角色真实运动状态提交给 Animator，并保存静止时的朝向。 */
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
    public sealed class LightPlayerAnimation : MonoBehaviour
    {
        /** @brief 接地参数标识；起跳的正向速度优先于脚下短暂重叠。 */
        private static readonly int _groundedId = Animator.StringToHash("Grounded");
        /** @brief 水平移动参数标识。 */
        private static readonly int _movingId = Animator.StringToHash("Moving");
        /** @brief 上升参数标识。 */
        private static readonly int _risingId = Animator.StringToHash("Rising");
        /** @brief 播放四个序列帧状态的动画组件。 */
        [SerializeField] private Animator _animator;
        /** @brief 通过翻转呈现左右朝向的精灵组件。 */
        [SerializeField] private SpriteRenderer _renderer;

        /** @brief 缓存同对象的表现组件。 */
        private void Awake()
        {
            if (!_animator) _animator = GetComponent<Animator>();
            if (!_renderer) _renderer = GetComponent<SpriteRenderer>();
        }

        /**
         * @brief 接收游戏入口的运动快照，暂停时保留当前动画帧和朝向。
         * @param velocity 角色刚体的真实速度，单位米每秒
         * @param grounded 游戏入口的接地判定
         * @param advancing 二维世界是否正在推进
         */
        public void UpdateMotion(Vector2 velocity, bool grounded, bool advancing)
        {
            if (!_animator || !_renderer || !_animator.runtimeAnimatorController)
                return;
            _animator.speed = advancing ? 1 : 0;
            if (!advancing)
                return;

            bool moving = Mathf.Abs(velocity.x) > .01f;
            bool rising = velocity.y > .01f;
            _animator.SetBool(_groundedId, grounded && !rising);
            _animator.SetBool(_movingId, moving);
            _animator.SetBool(_risingId, rising);
            if (moving)
                _renderer.flipX = velocity.x < 0;
        }

        /** @brief 重生时恢复默认闲置帧，清理上一段跳跃的动画播放位置。 */
        public void ResetPose()
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return;
            _animator.Rebind();
            _animator.Update(0);
        }
    }
}
