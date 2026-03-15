#include <iostream>
#include <cstdint>
#include <vector>
#include <string>
#include <onnxruntime_cxx_api.h>

struct YoloBoundingBox {
    float x;
    float y;
    float width;
    float height;
    float confidence;
    int classId;
};

static Ort::Env* ort_env = nullptr;
static Ort::Session* ort_session = nullptr;
static Ort::MemoryInfo* ort_memory_info = nullptr;
static std::string engine_info_cache;

extern "C" {

    const char* Yolo26_GetEngineInfo() {
        if (engine_info_cache.empty()) {
            engine_info_cache = std::string("ONNX Runtime v") + OrtGetApiBase()->GetVersionString() + " (CPU NativeAOT Zero-Copy)";
        }
        return engine_info_cache.c_str();
    }

    int Yolo26_Detect_Tensor(uint8_t* tensorData, int length, float threshold, YoloBoundingBox* detections, int maxDetections) {
        if (!ort_env) {
            std::cout << "[NATIVE CORE] Initializing ONNX Runtime Environment..." << std::endl;
            ort_env = new Ort::Env(ORT_LOGGING_LEVEL_WARNING, "Yolo26InferenceEngine");
            
            Ort::SessionOptions session_options;
            session_options.SetIntraOpNumThreads(1);
            session_options.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_EXTENDED);
            
            try {
                ort_session = new Ort::Session(*ort_env, "models/yolo26n.onnx", session_options);
                ort_memory_info = new Ort::MemoryInfo(Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault));
                std::cout << "[NATIVE CORE] ONNX Model loaded successfully." << std::endl;
            } catch(const std::exception& e) {
                std::cerr << "[NATIVE CORE] Failed to load models/yolo26n.onnx: " << e.what() << std::endl;
                return 0;
            }
        }
        
        if (!ort_session) return 0;
        if (maxDetections < 1) return 0;
        
        // TensorData is 1x3x640x640 floats = 1,228,800 floats = 4,915,200 bytes
        // In this architecture, it receives the pointer directly from C#.
        const int64_t input_shape[] = {1, 3, 640, 640};
        size_t input_tensor_size = 1 * 3 * 640 * 640;
        
        // Verify passed length match
        if (length < input_tensor_size * sizeof(float)) {
            // If the buffer is smaller, we can't safely cast
            return 0;
        }

        // 1. Create Input Tensor wrapped around the C# tensorData zero-copy pointer.
        Ort::Value input_tensor = Ort::Value::CreateTensor<float>(
            *ort_memory_info, 
            reinterpret_cast<float*>(tensorData), 
            input_tensor_size, 
            input_shape, 
            4
        );
        
        const char* input_names[] = {"images"}; 
        const char* output_names[] = {"output0"}; 
        
        // 2. Run Inference natively
        auto output_tensors = ort_session->Run(
            Ort::RunOptions{nullptr}, 
            input_names, 
            &input_tensor, 
            1, 
            output_names, 
            1
        );
        
        // 3. Process Output (YOLO structure is usually [1, 84, 8400] or [1, 300, 6] depending on the model export)
        // From our recent log: output shape(s) (1, 300, 6)
        float* output_arr = output_tensors.front().GetTensorMutableData<float>();
        
        int hits = 0;
        int num_proposals = 300; // Based on PyTorch exporter shape
        
        for (int i = 0; i < num_proposals && hits < maxDetections; ++i) {
            float* proposal = output_arr + (i * 6);
            
            // X, Y, W, H, Confidence, ClassId - based on shape [1, 300, 6]
            float x = proposal[0];
            float y = proposal[1];
            float w = proposal[2];
            float h = proposal[3];
            float conf = proposal[4];
            int classId = (int)proposal[5];
            
            if (conf >= threshold) {
                detections[hits].x = x;
                detections[hits].y = y;
                detections[hits].width = w;
                detections[hits].height = h;
                detections[hits].confidence = conf;
                detections[hits].classId = classId;
                hits++;
            }
        }
        
        return hits;
    }
}
