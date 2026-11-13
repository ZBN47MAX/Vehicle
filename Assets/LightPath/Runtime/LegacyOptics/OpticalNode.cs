using UnityEngine;

namespace DelayNoMore.LightPath
{
    /** @brief 绑定维修节点的光学规则；节点Transform归维修板管理。 */
    public sealed class OpticalNode : MonoBehaviour
    {
        /** @brief 规则资产。 */
        [SerializeField, Tooltip("该镜子或挡板的光学定义")] private OpticalNodeDefinition _definition;
        /** @brief 获取只读规则。 */
        public OpticalNodeDefinition Definition => _definition;
    }
}
