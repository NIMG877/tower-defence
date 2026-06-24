#ifndef TD_SPINE_ARKNIGHTS_DEPTH_INCLUDED
#define TD_SPINE_ARKNIGHTS_DEPTH_INCLUDED

float _DepthPoseAngle;
float _LowerDepthPoseAngle;
float _DepthSplitY;
float _LowerDepthBlend;
float _DepthOffset;

float4 TDSpineArknightsDepthClipPosition(float4 vertex) {
	float lowerBlend = saturate((_DepthSplitY - vertex.y) / max(_LowerDepthBlend, 0.0001));
	float angleRad = radians(lerp(_DepthPoseAngle, _LowerDepthPoseAngle, lowerBlend));
	float cosA = max(abs(cos(angleRad)), 0.0001);
	float sinA = sin(angleRad);

	float4 depthVertex = vertex;
	depthVertex.y /= cosA;

	float y = depthVertex.y;
	float z = depthVertex.z;
	depthVertex.y = y * cosA - z * sinA;
	depthVertex.z = y * sinA + z * cosA + _DepthOffset;

	float4 visualClip = UnityObjectToClipPos(vertex);
	float4 depthClip = UnityObjectToClipPos(depthVertex);
	visualClip.z = visualClip.w * depthClip.z / depthClip.w;
	return visualClip;
}

#endif
