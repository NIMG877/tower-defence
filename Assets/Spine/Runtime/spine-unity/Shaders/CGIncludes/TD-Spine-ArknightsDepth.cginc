#ifndef TD_SPINE_ARKNIGHTS_DEPTH_INCLUDED
#define TD_SPINE_ARKNIGHTS_DEPTH_INCLUDED

float _DepthPoseAngle;
float _LowerDepthPoseAngle;
float _DepthAnchorY;
float _DepthSplitY;
float _LowerDepthBlend;
float _DepthOffset;
float _MaxRayDepthCorrection;

float4 TDSpineArknightsDepthClipPosition(float4 vertex) {
	float lowerBlend = saturate((_DepthSplitY - vertex.y) / max(_LowerDepthBlend, 0.0001));
	float angleRad = radians(lerp(_DepthPoseAngle, _LowerDepthPoseAngle, lowerBlend));
	float tanA = tan(angleRad);

	float3 anchorLocal = float3(0.0, _DepthAnchorY, _DepthOffset);
	float3 depthXLocal = float3(1.0, 0.0, 0.0);
	float3 depthYLocal = float3(0.0, 1.0, tanA);

	float3 visualWorld = mul(unity_ObjectToWorld, vertex).xyz;
	float3 anchorWorld = mul(unity_ObjectToWorld, float4(anchorLocal, 1.0)).xyz;
	float3 depthXWorld = mul((float3x3)unity_ObjectToWorld, depthXLocal);
	float3 depthYWorld = mul((float3x3)unity_ObjectToWorld, depthYLocal);
	float3 depthNormalWorld = normalize(cross(depthXWorld, depthYWorld));

	float3 cameraWorld = _WorldSpaceCameraPos.xyz;
	float3 visualRay = normalize(visualWorld - cameraWorld);
	float denom = dot(visualRay, depthNormalWorld);
	float safeDenom = denom >= 0.0 ? max(denom, 0.0001) : min(denom, -0.0001);
	float rayT = dot(anchorWorld - cameraWorld, depthNormalWorld) / safeDenom;

	float3 hitWorld = cameraWorld + visualRay * rayT;
	float4 proxyLocal = float4(vertex.x, vertex.y, vertex.z + (vertex.y - _DepthAnchorY) * tanA + _DepthOffset, 1.0);
	float3 proxyWorld = mul(unity_ObjectToWorld, proxyLocal).xyz;

	float frontBlend = step(0.0, rayT);
	float maxCorrection = max(_MaxRayDepthCorrection, 0.0001);
	float correctionDistance = distance(hitWorld, proxyWorld);
	float correctionBlend = 1.0 - smoothstep(maxCorrection, maxCorrection * 2.0, correctionDistance);
	float stableBlend = frontBlend * correctionBlend;
	hitWorld = lerp(proxyWorld, hitWorld, stableBlend);

	float4 visualClip = UnityObjectToClipPos(vertex);
	float4 depthClip = mul(UNITY_MATRIX_VP, float4(hitWorld, 1.0));
	visualClip.z = visualClip.w * depthClip.z / depthClip.w;
	return visualClip;
}

#endif
