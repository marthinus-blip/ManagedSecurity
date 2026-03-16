#include <iostream>
#include <cstdint>
#include <vector>
#include <string>
#include <onnxruntime_cxx_api.h>

#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#define STB_IMAGE_RESIZE_IMPLEMENTATION
#include "stb_image_resize2.h"

static Ort::Env* ort_env = nullptr;
static Ort::Session* ort_session = nullptr;
static Ort::MemoryInfo* ort_memory_info = nullptr;
static std::string engine_info_cache;

static std::vector<int64_t> ort_input_shape;
static int64_t model_width = 0;
static int64_t model_height = 0;
static int64_t model_channels = 0;
static int64_t model_proposals = 0;
static int64_t model_proposal_elements = 0;
static size_t model_tensor_size = 0;

struct YoloBoundingBox {
    float x;
    float y;
    float width;
    float height;
    float confidence;
    int classId;
};



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
                
                // Dynamically read model input constraints
                Ort::TypeInfo type_info = ort_session->GetInputTypeInfo(0);
                auto tensor_info = type_info.GetTensorTypeAndShapeInfo();
                ort_input_shape = tensor_info.GetShape();
                if (ort_input_shape[0] < 1) ort_input_shape[0] = 1; // Batch size
                
                model_channels = ort_input_shape[1];
                model_height = ort_input_shape[2];
                model_width = ort_input_shape[3];
                model_tensor_size = ort_input_shape[0] * model_channels * model_height * model_width;

                // Dynamically read model output constraints
                Ort::TypeInfo out_info = ort_session->GetOutputTypeInfo(0);
                auto out_tensor_info = out_info.GetTensorTypeAndShapeInfo();
                std::vector<int64_t> out_shape = out_tensor_info.GetShape();
                if (out_shape.size() >= 3) {
                    model_proposals = out_shape[1];
                    model_proposal_elements = out_shape[2];
                }
                
                std::cout << "[NATIVE CORE] ONNX Model loaded. Input: " << model_width << "x" << model_height 
                          << ", Proposals: " << model_proposals << "x" << model_proposal_elements << std::endl;
            } catch(const std::exception& e) {
                std::cerr << "[NATIVE CORE] Failed to load models/yolo26n.onnx: " << e.what() << std::endl;
                return 0;
            }
        }
        
        if (!ort_session) return 0;
        if (maxDetections < 1) return 0;
        
        // TensorData is expected to match network input shape and channels
        // In this architecture, it receives the pointer directly from C#.
        
        // Verify passed length match
        float* inferenceData = reinterpret_cast<float*>(tensorData);
        static std::vector<float> input_tensor_values;
        if (input_tensor_values.size() != model_tensor_size) {
            input_tensor_values.resize(model_tensor_size);
        }

        if (length < model_tensor_size * sizeof(float)) {
            int img_width, img_height, img_channels;
            unsigned char* img_data = stbi_load_from_memory(tensorData, length, &img_width, &img_height, &img_channels, model_channels);
            
            if (!img_data) {
                return 0; // Invalid image or empty tensor
            }

            unsigned char* resized_data = img_data;
            if (img_width != model_width || img_height != model_height) {
                resized_data = new unsigned char[model_width * model_height * model_channels];
                stbir_resize_uint8_linear(img_data, img_width, img_height, 0,
                                          resized_data, model_width, model_height, 0,
                                          (stbir_pixel_layout)STBIR_RGB);
                stbi_image_free(img_data);
            }

            // Convert HWC to CHW planar format correctly mapped to normalized float32
            for (int y = 0; y < model_height; ++y) {
                for (int x = 0; x < model_width; ++x) {
                    int pixel_idx = (y * model_width + x) * model_channels;
                    input_tensor_values[0 * model_height * model_width + y * model_width + x] = resized_data[pixel_idx + 0] / 255.0f; // R
                    input_tensor_values[1 * model_height * model_width + y * model_width + x] = resized_data[pixel_idx + 1] / 255.0f; // G
                    input_tensor_values[2 * model_height * model_width + y * model_width + x] = resized_data[pixel_idx + 2] / 255.0f; // B
                }
            }

            if (resized_data != img_data) {
                delete[] resized_data;
            } else {
                stbi_image_free(img_data);
            }

            inferenceData = input_tensor_values.data();
        }

        // 1. Create Input Tensor wrapped around the processed or passed pointer.
        Ort::Value input_tensor = Ort::Value::CreateTensor<float>(
            *ort_memory_info, 
            inferenceData, 
            model_tensor_size, 
            ort_input_shape.data(), 
            ort_input_shape.size()
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
        
        // 3. Process Output
        float* output_arr = output_tensors.front().GetTensorMutableData<float>();
        
        int hits = 0;
        
        bool is_transposed = false;
        int64_t num_proposals = model_proposals;
        int64_t num_elements = model_proposal_elements;
        
        if (model_proposals < model_proposal_elements) {
            is_transposed = true;
            num_proposals = model_proposal_elements; // e.g. 8400
            num_elements = model_proposals; // e.g. 84
        }
        
        for (int i = 0; i < num_proposals && hits < maxDetections; ++i) {
            float x, y, w, h;
            float conf = 0.0f;
            int classId = 0;
            
            if (is_transposed) {
                // YOLO End2End ONNX exports [x_min, y_min, x_max, y_max, confidence, classId]
                float x_min = output_arr[0 * num_proposals + i];
                float y_min = output_arr[1 * num_proposals + i];
                float x_max = output_arr[2 * num_proposals + i];
                float y_max = output_arr[3 * num_proposals + i];
                
                // Convert to cx, cy, w, h for the UI
                w = x_max - x_min;
                h = y_max - y_min;
                x = x_min + w / 2.0f;
                y = y_min + h / 2.0f;
                
                for (int c = 4; c < num_elements; ++c) {
                    float class_prob = output_arr[c * num_proposals + i];
                    if (class_prob > conf) {
                        conf = class_prob;
                        classId = c - 4;
                    }
                }
            } else {
                float* proposal = output_arr + (i * num_elements);
                // YOLO End2End ONNX exports [x_min, y_min, x_max, y_max, confidence, classId]
                float x_min = proposal[0];
                float y_min = proposal[1];
                float x_max = proposal[2];
                float y_max = proposal[3];
                conf = proposal[4];
                classId = (int)proposal[5];
                
                // Convert to cx, cy, w, h for the UI
                w = x_max - x_min;
                h = y_max - y_min;
                x = x_min + w / 2.0f;
                y = y_min + h / 2.0f;
            }
            
            if (conf >= threshold) {
                detections[hits].x = x / static_cast<float>(model_width);
                detections[hits].y = y / static_cast<float>(model_height);
                detections[hits].width = w / static_cast<float>(model_width);
                detections[hits].height = h / static_cast<float>(model_height);
                detections[hits].confidence = conf;
                detections[hits].classId = classId;
                hits++;
            }
        }
        
        return hits;
    }
}
