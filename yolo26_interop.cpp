#include <iostream>
#include <cstdint>

// Define the struct exactly as the C# side expects it
struct YoloBoundingBox {
    float x;
    float y;
    float width;
    float height;
    float confidence;
    int classId;
};

extern "C" {
    // The exact signature expected by C# [LibraryImport]
    // return value: number of detections
    int Yolo26_Detect_Tensor(uint8_t* tensorData, int length, float threshold, YoloBoundingBox* detections, int maxDetections) {
        
        // This is where ONNX Runtime / TensorRT execution occurs.
        // For demonstration, we simulate parsing a tensor and returning one valid box.
        std::cout << "[NATIVE CORE] Tensor received (" << length << " bytes). Threshold: " << threshold << std::endl;
        
        if (maxDetections < 1) return 0;
        
        // Artificial native detection (Class 0 = Person)
        detections[0].x = 0.5f;
        detections[0].y = 0.5f;
        detections[0].width = 0.25f;
        detections[0].height = 0.75f;
        detections[0].confidence = threshold + 0.1f;
        detections[0].classId = 0; 
        
        return 1; // Return 1 detection
    }
}
