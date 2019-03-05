// Taken from https://social.msdn.microsoft.com/Forums/en-US/ffbc8ec7-7551-4462-88aa-2fab69eac38f/joint-smoothing-code-c-errors-in-kinectjointfilter-class?forum=kinectv2sdk

using System;
using Microsoft.Kinect;

public class KinectJointFilter
{
    float mFCorrection;
    float mFJitterRadius;
    float mFMaxDeviationRadius;
    float mFPrediction;
    float mFSmoothing;

    // Holt Double Exponential Smoothing filter
    readonly CameraSpacePoint[] mPFilteredJoints;
    readonly FilterDoubleExponentialData[] mPHistory;

    public KinectJointFilter()
    {
        mPFilteredJoints = new CameraSpacePoint[Body.JointCount];
        mPHistory = new FilterDoubleExponentialData[Body.JointCount];
        for (var i = 0; i < Body.JointCount; i++)
            mPHistory[i] = new FilterDoubleExponentialData();

        Init();
    }

    public CameraSpacePoint[] GetFilteredJoints() => mPFilteredJoints;

    public void Init(float fSmoothing = 0.25f,
                     float fCorrection = 0.25f,
                     float fPrediction = 0.25f,
                     float fJitterRadius = 0.03f,
                     float fMaxDeviationRadius = 0.05f)
    {
        Reset(fSmoothing, fCorrection, fPrediction, fJitterRadius, fMaxDeviationRadius);
    }

    public void Reset(float fSmoothing = 0.25f,
                      float fCorrection = 0.25f,
                      float fPrediction = 0.25f,
                      float fJitterRadius = 0.03f,
                      float fMaxDeviationRadius = 0.05f)
    {
        if (mPFilteredJoints == null || mPHistory == null)
            return;

        mFMaxDeviationRadius =
            fMaxDeviationRadius; // Size of the max prediction radius Can snap back to noisy data when too high
        mFSmoothing = fSmoothing; // How much smothing will occur.  Will lag when too high
        mFCorrection = fCorrection; // How much to correct back from prediction.  Can make things springy
        mFPrediction = fPrediction; // Amount of prediction into the future to use. Can over shoot when too high
        mFJitterRadius =
            fJitterRadius; // Size of the radius where jitter is removed. Can do too much smoothing when too high

        for (var i = 0; i < Body.JointCount; i++)
        {
            mPFilteredJoints[i].X = 0.0f;
            mPFilteredJoints[i].Y = 0.0f;
            mPFilteredJoints[i].Z = 0.0f;

            mPHistory[i].MvFilteredPosition.X = 0.0f;
            mPHistory[i].MvFilteredPosition.Y = 0.0f;
            mPHistory[i].MvFilteredPosition.Z = 0.0f;

            mPHistory[i].MvRawPosition.X = 0.0f;
            mPHistory[i].MvRawPosition.Y = 0.0f;
            mPHistory[i].MvRawPosition.Z = 0.0f;

            mPHistory[i].MvTrend.X = 0.0f;
            mPHistory[i].MvTrend.Y = 0.0f;
            mPHistory[i].MvTrend.Z = 0.0f;

            mPHistory[i].MDwFrameCount = 0;
        }
    }

    public void Shutdown() { }

    //--------------------------------------------------------------------------------------
    // Implementation of a Holt Double Exponential Smoothing filter. The double exponential
    // smooths the curve and predicts.  There is also noise jitter removal. And maximum
    // prediction bounds.  The paramaters are commented in the init function.
    //--------------------------------------------------------------------------------------
    public void UpdateFilter(Body pBody)
    {
        if (pBody == null)
            return;

        // Check for divide by zero. Use an epsilon of a 10th of a millimeter
        mFJitterRadius = Math.Max(0.0001f, mFJitterRadius);

        for (var jt = JointType.SpineBase; jt <= JointType.ThumbRight; jt++)
        {
            TransformSmoothParameters smoothingParams;
            smoothingParams.FSmoothing = mFSmoothing;
            smoothingParams.FCorrection = mFCorrection;
            smoothingParams.FPrediction = mFPrediction;
            smoothingParams.FJitterRadius = mFJitterRadius;
            smoothingParams.FMaxDeviationRadius = mFMaxDeviationRadius;

            // If inferred, we smooth a bit more by using a bigger jitter radius
            var joint = pBody.Joints[jt];
            if (joint.TrackingState == TrackingState.Inferred)
            {
                smoothingParams.FJitterRadius *= 2.0f;
                smoothingParams.FMaxDeviationRadius *= 2.0f;
            }

            UpdateJoint(pBody, jt, smoothingParams);
        }
    }

    CameraSpacePoint CsVectorAdd(CameraSpacePoint p1, CameraSpacePoint p2)
    {
        var sum = new CameraSpacePoint
        {
            X = p1.X + p2.X,
            Y = p1.Y + p2.Y,
            Z = p1.Z + p2.Z
        };

        return sum;
    }

    float CsVectorLength(CameraSpacePoint p) => Convert.ToSingle(Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z));

    CameraSpacePoint CsVectorScale(CameraSpacePoint p, float scale)
    {
        var point = new CameraSpacePoint
        {
            X = p.X * scale,
            Y = p.Y * scale,
            Z = p.Z * scale
        };

        return point;
    }

    CameraSpacePoint CsVectorSet(float x, float y, float z)
    {
        var point = new CameraSpacePoint
        {
            X = x,
            Y = y,
            Z = z
        };

        return point;
    }

    CameraSpacePoint CsVectorSubtract(CameraSpacePoint p1, CameraSpacePoint p2)
    {
        var diff = new CameraSpacePoint
        {
            X = p1.X - p2.X,
            Y = p1.Y - p2.Y,
            Z = p1.Z - p2.Z
        };

        return diff;
    }

    CameraSpacePoint CsVectorZero()
    {
        var point = new CameraSpacePoint {X = 0.0f, Y = 0.0f, Z = 0.0f};


        return point;
    }

    const float EPSILON = 0.0001f;

    //--------------------------------------------------------------------------------------
    // if joint is 0 it is not valid.
    //--------------------------------------------------------------------------------------
    bool JointPositionIsValid(CameraSpacePoint vJointPosition)
    {
        return Math.Abs(vJointPosition.X) > EPSILON || Math.Abs(vJointPosition.Y) > EPSILON || Math.Abs(vJointPosition.Z) > EPSILON;
    }

    void UpdateJoint(Body body, JointType jt, TransformSmoothParameters smoothingParams)
    {
        CameraSpacePoint vFilteredPosition;
        CameraSpacePoint vDiff;
        CameraSpacePoint vTrend;
        float fDiff;

        var joint = body.Joints[jt];

        var vRawPosition = joint.Position;
        var vPrevFilteredPosition = mPHistory[(int) jt].MvFilteredPosition;
        var vPrevTrend = mPHistory[(int) jt].MvTrend;
        var vPrevRawPosition = mPHistory[(int) jt].MvRawPosition;
        var bJointIsValid = JointPositionIsValid(vRawPosition);

        // If joint is invalid, reset the filter
        if (!bJointIsValid)
            mPHistory[(int) jt].MDwFrameCount = 0;

        // Initial start values
        if (mPHistory[(int) jt].MDwFrameCount == 0)
        {
            vFilteredPosition = vRawPosition;
            vTrend = CsVectorZero();
            mPHistory[(int) jt].MDwFrameCount++;
        }
        else if (mPHistory[(int) jt].MDwFrameCount == 1)
        {
            vFilteredPosition = CsVectorScale(CsVectorAdd(vRawPosition, vPrevRawPosition), 0.5f);
            vDiff = CsVectorSubtract(vFilteredPosition, vPrevFilteredPosition);
            vTrend = CsVectorAdd(CsVectorScale(vDiff, smoothingParams.FCorrection),
                                 CsVectorScale(vPrevTrend, 1.0f - smoothingParams.FCorrection));
            mPHistory[(int) jt].MDwFrameCount++;
        }
        else
        {
            // First apply jitter filter
            vDiff = CsVectorSubtract(vRawPosition, vPrevFilteredPosition);
            fDiff = CsVectorLength(vDiff);

            if (fDiff <= smoothingParams.FJitterRadius)
                vFilteredPosition = CsVectorAdd(CsVectorScale(vRawPosition, fDiff / smoothingParams.FJitterRadius),
                                                CsVectorScale(vPrevFilteredPosition,
                                                              1.0f - fDiff / smoothingParams.FJitterRadius));
            else
                vFilteredPosition = vRawPosition;

            // Now the double exponential smoothing filter
            vFilteredPosition = CsVectorAdd(CsVectorScale(vFilteredPosition, 1.0f - smoothingParams.FSmoothing),
                                            CsVectorScale(CsVectorAdd(vPrevFilteredPosition, vPrevTrend),
                                                          smoothingParams.FSmoothing));


            vDiff = CsVectorSubtract(vFilteredPosition, vPrevFilteredPosition);
            vTrend = CsVectorAdd(CsVectorScale(vDiff, smoothingParams.FCorrection),
                                 CsVectorScale(vPrevTrend, 1.0f - smoothingParams.FCorrection));
        }

        // Predict into the future to reduce latency
        var vPredictedPosition = CsVectorAdd(vFilteredPosition, CsVectorScale(vTrend, smoothingParams.FPrediction));

        // Check that we are not too far away from raw data
        vDiff = CsVectorSubtract(vPredictedPosition, vRawPosition);
        fDiff = CsVectorLength(vDiff);

        if (fDiff > smoothingParams.FMaxDeviationRadius)
            vPredictedPosition =
                CsVectorAdd(CsVectorScale(vPredictedPosition, smoothingParams.FMaxDeviationRadius / fDiff),
                            CsVectorScale(vRawPosition, 1.0f - smoothingParams.FMaxDeviationRadius / fDiff));

        // Save the data from this frame
        mPHistory[(int) jt].MvRawPosition = vRawPosition;
        mPHistory[(int) jt].MvFilteredPosition = vFilteredPosition;
        mPHistory[(int) jt].MvTrend = vTrend;

        // Output the data
        mPFilteredJoints[(int) jt] = vPredictedPosition;
    }

    ~KinectJointFilter() { Shutdown(); }

    public struct TransformSmoothParameters
    {
        public float FSmoothing; // [0..1], lower values closer to raw data
        public float FCorrection; // [0..1], lower values slower to correct towards the raw data
        public float FPrediction; // [0..n], the number of frames to predict into the future
        public float FJitterRadius; // The radius in meters for jitter reduction

        public float
            FMaxDeviationRadius; // The maximum radius in meters that filtered positions are allowed to deviate from raw data
    }

    public class FilterDoubleExponentialData
    {
        public int MDwFrameCount;
        public CameraSpacePoint MvFilteredPosition;
        public CameraSpacePoint MvRawPosition;
        public CameraSpacePoint MvTrend;
    }
}